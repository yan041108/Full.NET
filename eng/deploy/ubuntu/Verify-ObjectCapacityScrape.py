#!/usr/bin/env python3
"""只读核对 Prometheus 中的一个对象容量目标；不改配置、不发通知。"""
import argparse
import json
import math
import os
from pathlib import Path
import re
import signal
import ssl
import stat
import sys
import time
import urllib.parse
import urllib.request

PREFIX = 'fullnet_object_storage_capacity_'
SCRAPE_TIMESTAMP = 'fullnet_verification_scrape_timestamp_seconds'
CAPACITY = [PREFIX + suffix for suffix in ('probe_success', 'sample_timestamp_seconds', 'growth_state', 'free_slots', 'free_bytes')]
METRICS = ['up', 'node_textfile_scrape_error', *CAPACITY]
STATES = {0: 'growth_headroom_available', 1: 'growth_at_risk', 2: 'growth_blocked'}


class VerificationFailure(Exception):
    """固定失败码，不回显 API 响应、URL、凭据或底层异常。"""


def require(condition, code):
    if not condition:
        raise VerificationFailure(code)


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, _request, _stream, _code, _message, _headers, _url):
        return None


def endpoint(url, ca_file=None, token_file=None):
    parsed = urllib.parse.urlsplit(url)
    require(parsed.scheme in ('http', 'https') and parsed.hostname and not parsed.username and not parsed.password
        and not parsed.query and not parsed.fragment and not any(character.isspace() for character in url), 'prometheus_url_invalid')
    require(parsed.port is None or 1 <= parsed.port <= 65535, 'prometheus_url_invalid')
    if parsed.scheme == 'http':
        require(parsed.hostname in ('127.0.0.1', 'localhost', '::1') and not ca_file and not token_file,
            'plain_http_requires_loopback_without_credentials')
    return url.rstrip('/') + '/api/v1/query'


def token(path):
    descriptor = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(descriptor, 'rb') as stream:
        info = os.fstat(stream.fileno())
        require(stat.S_ISREG(info.st_mode) and info.st_nlink == 1 and info.st_uid == os.geteuid()
            and info.st_mode & 0o077 == 0 and 0 < info.st_size <= 4096, 'token_file_invalid')
        content = stream.read(4097)
        require(len(content) == info.st_size, 'token_file_invalid')
        value = content.decode('ascii').strip()
        require(re.fullmatch(r'[A-Za-z0-9._~+/=-]{1,4096}', value) is not None, 'token_file_invalid')
        return value


def labels(cluster, instance):
    require(re.fullmatch(r'[a-z0-9](?:[a-z0-9._-]{0,62})', cluster) is not None, 'cluster_invalid')
    require(re.fullmatch(r'(?=.{1,259}\Z)[a-z0-9](?:[a-z0-9.-]*[a-z0-9])?:[0-9]{1,5}', instance) is not None
        and 1 <= int(instance.rsplit(':', 1)[1]) <= 65535, 'instance_invalid')
    return {'job': 'fullnet-object-capacity', 'cluster': cluster, 'instance': instance}


def expression(expected):
    selector = ','.join(key + '=' + json.dumps(value) for key, value in expected.items())
    # 将原始 up 样本时间作为独立序列附在同一次查询，避免两次查询跨越一次抓取。
    return ('{__name__=~' + json.dumps('|'.join(METRICS)) + ',' + selector + '} or on(__name__,job,cluster,instance) '
        'label_replace(timestamp(up{' + selector + '}),"__name__",' + json.dumps(SCRAPE_TIMESTAMP) + ',"job",".*")')


def query(url, expected, now, ca_file=None, token_file=None):
    destination = endpoint(url, ca_file, token_file)
    context = ssl.create_default_context(cafile=ca_file) if destination.startswith('https://') else None
    headers = {'Accept': 'application/json', 'Accept-Encoding': 'identity'}
    if token_file:
        headers['Authorization'] = 'Bearer ' + token(token_file)
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect(),
        urllib.request.HTTPSHandler(context=context))
    request = urllib.request.Request(destination + '?' + urllib.parse.urlencode({
        'query': expression(expected), 'time': f'{now:.6f}', 'timeout': '3s'}), headers=headers)
    try:
        with opener.open(request, timeout=5) as response:
            require(response.status == 200 and response.headers.get_content_type() == 'application/json'
                and response.headers.get('Content-Encoding', 'identity') == 'identity', 'api_response_invalid')
            content = response.read(65537)
            require(len(content) <= 65536, 'api_response_too_large')
        value = json.loads(content)
    except (OSError, ValueError):
        raise VerificationFailure('api_unavailable') from None
    require(isinstance(value, dict) and value.get('status') == 'success' and not value.get('warnings')
        and not value.get('infos') and not value.get('error'), 'api_response_invalid')
    data = value.get('data')
    require(isinstance(data, dict) and data.get('resultType') == 'vector' and isinstance(data.get('result'), list)
        and len(data['result']) <= 16, 'api_response_invalid')
    return data['result']


def number(value):
    require(type(value) in (int, float, str), 'sample_invalid')
    try:
        result = float(value)
    except (ValueError, OverflowError):
        raise VerificationFailure('sample_invalid') from None
    require(math.isfinite(result), 'sample_invalid')
    return result


def evaluate(samples, expected, now):
    values = {}
    for item in samples:
        require(isinstance(item, dict) and isinstance(item.get('metric'), dict), 'sample_invalid')
        name = item['metric'].get('__name__')
        require(name in (*METRICS, SCRAPE_TIMESTAMP) and name not in values, 'sample_ambiguous')
        require(item['metric'] == {**expected, '__name__': name}, 'sample_identity_invalid')
        point = item.get('value')
        require(isinstance(point, list) and len(point) == 2 and abs(number(point[0]) - now) <= 1, 'sample_time_invalid')
        values[name] = number(point[1])
    def result(state, code, **details):
        return {'state': state, 'cluster': expected['cluster'], 'instance': expected['instance'],
            'observedAt': round(now, 3), **details}, code
    if not values:
        return result('target_missing', 2)
    require(values.get('up') in (0, 1), 'target_state_invalid')
    if values['up'] == 0:
        return result('target_down', 2)
    require(SCRAPE_TIMESTAMP in values, 'scrape_time_missing')
    scrape_age = now - values[SCRAPE_TIMESTAMP]
    require(scrape_age >= -60, 'scrape_time_invalid')
    if scrape_age > 60:
        return result('scrape_stale', 2, scrapeAgeSeconds=round(scrape_age, 3))
    require(values.get('node_textfile_scrape_error') in (0, 1), 'textfile_state_missing')
    if values['node_textfile_scrape_error'] == 1:
        return result('textfile_unavailable', 2)
    require(PREFIX + 'probe_success' in values and PREFIX + 'sample_timestamp_seconds' in values, 'capacity_metrics_missing')
    require(values[PREFIX + 'probe_success'] in (0, 1), 'probe_state_invalid')
    capacity_names = [PREFIX + suffix for suffix in ('growth_state', 'free_slots', 'free_bytes')]
    if values[PREFIX + 'probe_success'] == 0:
        require(not any(name in values for name in capacity_names), 'failed_probe_has_old_capacity')
        return result('probe_unavailable', 2)
    sample_age = now - values[PREFIX + 'sample_timestamp_seconds']
    require(sample_age >= -60, 'capacity_sample_time_invalid')
    if sample_age >= 720:
        return result('capacity_sample_stale', 2, sampleAgeSeconds=round(sample_age, 3))
    require(all(name in values for name in capacity_names), 'capacity_metrics_missing')
    growth, slots, free = [values[name] for name in capacity_names]
    require(growth in STATES and all(value.is_integer() and 0 <= value <= 2**53 for value in (slots, free)), 'capacity_value_invalid')
    # 容量判断由正式巡检发布；此处只解释稳定状态码，不复制卷槽/磁盘阈值算法。
    return result(STATES[growth], 0 if growth == 0 else 1, scrapeAgeSeconds=round(scrape_age, 3),
        sampleAgeSeconds=round(sample_age, 3), growthState=int(growth), freeSlots=int(slots), freeBytes=int(free))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--prometheus-url', required=True)
    parser.add_argument('--cluster', required=True)
    parser.add_argument('--instance', required=True)
    parser.add_argument('--ca-file', type=Path)
    parser.add_argument('--token-file', type=Path)
    options = parser.parse_args()
    def expired(_number, _frame):
        raise VerificationFailure('verification_deadline_exceeded')
    previous = signal.signal(signal.SIGALRM, expired)
    signal.setitimer(signal.ITIMER_REAL, 8)
    try:
        expected = labels(options.cluster, options.instance)
        now = time.time()
        report, code = evaluate(query(options.prometheus_url, expected, now, options.ca_file, options.token_file), expected, now)
        print(json.dumps(report, allow_nan=False))
        return code
    except (VerificationFailure, OSError, ValueError):
        error = sys.exc_info()[1]
        print(json.dumps({'state': 'verification_failed', 'code': str(error) if isinstance(error, VerificationFailure)
            else 'local_configuration_invalid'}))
        return 2
    finally:
        signal.setitimer(signal.ITIMER_REAL, 0)
        signal.signal(signal.SIGALRM, previous)


if __name__ == '__main__':
    sys.exit(main())
