from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONTRACT_ROOT = Path(__file__).parent
PLACEHOLDER_PATTERN = re.compile(
    r"\b(?:TB[D]|TO[DO]|FIXM[E])\b|implement\s+later|fill\s+in\s+details",
    re.IGNORECASE,
)


def read_utf8(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def collect_terms(contract: dict, field: str) -> list[str]:
    """汇总契约级与 scenario 级术语，保持声明顺序并去重。"""
    terms = list(contract.get(field, []))
    for scenario in contract.get("scenarios", []):
        terms.extend(scenario.get(field, []))
    return list(dict.fromkeys(terms))


def find_present_terms(text: str, terms: list[str]) -> list[str]:
    """返回文本中实际出现的术语；使用子串匹配，避免把禁止句误判为违规句。"""
    return [term for term in terms if term in text]


def validate_contract(contract_path: Path) -> list[str]:
    contract = json.loads(read_utf8(contract_path))
    skill_name = contract["skill"]
    skill_dir = ROOT / ".agents" / "skills" / skill_name
    errors: list[str] = []

    if not skill_dir.is_dir():
        relative = skill_dir.relative_to(ROOT).as_posix()
        return [f"Missing skill directory: {relative}"]

    skill_path = skill_dir / "SKILL.md"
    metadata_path = skill_dir / "agents" / "openai.yaml"
    reference_relatives = contract.get("references")
    if reference_relatives is None:
        reference_relatives = [
            contract.get("reference", "references/delivery-map.md")
        ]
    reference_paths = [skill_dir / relative for relative in reference_relatives]

    for path in (skill_path, metadata_path, *reference_paths):
        if not path.is_file():
            errors.append(f"Missing required file: {path.relative_to(ROOT).as_posix()}")

    if errors:
        return errors

    skill_text = read_utf8(skill_path)
    metadata_text = read_utf8(metadata_path)
    reference_texts = [read_utf8(path) for path in reference_paths]
    combined_text = "\n".join([skill_text, *reference_texts])

    frontmatter = re.match(r"\A---\r?\n(.*?)\r?\n---\r?\n", skill_text, re.DOTALL)
    if frontmatter is None:
        errors.append("SKILL.md has no valid YAML frontmatter.")
    else:
        lines = [line for line in frontmatter.group(1).splitlines() if line.strip()]
        keys = [line.split(":", 1)[0].strip() for line in lines if ":" in line]
        fields = {
            line.split(":", 1)[0].strip(): line.split(":", 1)[1].strip()
            for line in lines
            if ":" in line
        }
        if keys != ["name", "description"]:
            errors.append(f"Frontmatter keys must be name, description; got {keys}.")
        if fields.get("name") != skill_name:
            errors.append(f"Frontmatter name must be {skill_name}.")
        if not fields.get("description", "").startswith("Use when "):
            errors.append("Frontmatter description must start with 'Use when '.")

    line_count = len(skill_text.splitlines())
    if line_count > contract["max_lines"]:
        errors.append(f"SKILL.md has {line_count} lines; maximum is {contract['max_lines']}.")
    if PLACEHOLDER_PATTERN.search(combined_text):
        errors.append("Skill content contains a placeholder marker.")
    for reference_relative in reference_relatives:
        if f"({reference_relative})" not in skill_text:
            errors.append(f"SKILL.md must link directly to {reference_relative}.")
    if f"${skill_name}" not in metadata_text:
        errors.append(f"agents/openai.yaml must mention ${skill_name} in default_prompt.")

    required_terms = collect_terms(contract, "required_terms")
    for term in required_terms:
        if term not in combined_text:
            errors.append(f"Missing contract term: {term}")

    # 每个 Skill 可声明自己的结构化门禁，避免把特定领域规则硬编码进通用校验器。
    for term in contract.get("verification_terms", []):
        if term not in combined_text:
            errors.append(f"Missing verification term: {term}")

    forbidden_terms = collect_terms(contract, "forbidden_terms")
    for term in find_present_terms(combined_text, forbidden_terms):
        errors.append(f"Forbidden contract term present: {term}")

    if not errors:
        print(
            f"PASS {skill_name}: "
            f"{len(required_terms)} contract checks"
        )
    return errors


def self_test_forbidden_terms() -> list[str]:
    """覆盖 forbidden_terms 正反例，防止把“禁止缓存 Outbox”等正确指引误判为违规。"""
    errors: list[str] = []
    forbidden = [
        "缓存失效由提交后的 Outbox",
        "缓存失效由 Outbox",
        "由 Outbox 事件触发",
        "提交后失效 Handler",
    ]
    bad_samples = [
        "缓存失效由提交后的 Outbox 事件触发，保证多实例 L1/L2 与 Backplane 一致",
        "缓存失效由 Outbox 触发",
        "由 Outbox 事件触发缓存失效",
        "模块缓存消费者、租户化 Key、提交后失效 Handler、Unit/Integration Tests",
    ]
    good_samples = [
        "缓存失效不写 Outbox",
        "缓存失效禁止使用 Outbox",
        "禁止缓存 Outbox",
        "重要业务事件才允许 Outbox",
        "事务提交后直接删除 L1/L2 并广播 Backplane",
        "Cache invalidation: commit database state -> remove current L1/shared L2 -> publish Backplane.",
    ]

    for sample in bad_samples:
        hits = find_present_terms(sample, forbidden)
        if not hits:
            errors.append(
                f"forbidden_terms self-test expected a hit in bad sample: {sample!r}"
            )

    for sample in good_samples:
        hits = find_present_terms(sample, forbidden)
        if hits:
            errors.append(
                "forbidden_terms self-test false-positive "
                f"on {sample!r}: {hits}"
            )

    # 契约级与 scenario 级收集必须可叠加且去重。
    contract = {
        "forbidden_terms": ["缓存失效由 Outbox"],
        "scenarios": [
            {"forbidden_terms": ["缓存失效由 Outbox", "提交后失效 Handler"]},
        ],
    }
    collected = collect_terms(contract, "forbidden_terms")
    if collected != ["缓存失效由 Outbox", "提交后失效 Handler"]:
        errors.append(f"collect_terms self-test unexpected result: {collected}")

    if not errors:
        print("PASS forbidden_terms self-test: positive and negative samples")
    return errors


def main() -> int:
    errors: list[str] = []
    errors.extend(self_test_forbidden_terms())

    contract_paths = sorted(CONTRACT_ROOT.glob("*.contract.json"))
    for contract_path in contract_paths:
        errors.extend(validate_contract(contract_path))

    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
