from __future__ import annotations

import json
import re
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONTRACT_PATH = Path(__file__).with_name("fullnet-module-delivery.contract.json")
PLACEHOLDER_PATTERN = re.compile(
    r"\b(?:TB[D]|TO[DO]|FIXM[E])\b|implement\s+later|fill\s+in\s+details",
    re.IGNORECASE,
)


def read_utf8(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def main() -> int:
    contract = json.loads(read_utf8(CONTRACT_PATH))
    skill_name = contract["skill"]
    skill_dir = ROOT / ".agents" / "skills" / skill_name
    errors: list[str] = []

    if not skill_dir.is_dir():
        relative = skill_dir.relative_to(ROOT).as_posix()
        print(f"Missing skill directory: {relative}", file=sys.stderr)
        return 1

    skill_path = skill_dir / "SKILL.md"
    metadata_path = skill_dir / "agents" / "openai.yaml"
    reference_path = skill_dir / "references" / "delivery-map.md"

    for path in (skill_path, metadata_path, reference_path):
        if not path.is_file():
            errors.append(f"Missing required file: {path.relative_to(ROOT).as_posix()}")

    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1

    skill_text = read_utf8(skill_path)
    metadata_text = read_utf8(metadata_path)
    reference_text = read_utf8(reference_path)
    combined_text = f"{skill_text}\n{reference_text}"

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
    if "(references/delivery-map.md)" not in skill_text:
        errors.append("SKILL.md must link directly to references/delivery-map.md.")
    if f"${skill_name}" not in metadata_text:
        errors.append(f"agents/openai.yaml must mention ${skill_name} in default_prompt.")

    required_terms = list(contract["required_terms"])
    for scenario in contract["scenarios"]:
        required_terms.extend(scenario["required_terms"])
    for term in dict.fromkeys(required_terms):
        if term not in combined_text:
            errors.append(f"Missing contract term: {term}")

    # 模块交付 Skill 必须把验证成本按风险分层，避免后续会话恢复为
    # “任意局部改动都先跑 172 项 Integration”的低反馈效率路径。
    for term in ("变更风险分层", "全量触发条件", "test:integration:full"):
        if term not in combined_text:
            errors.append(f"Missing layered verification term: {term}")

    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1

    print(f"PASS {skill_name}: {len(tuple(dict.fromkeys(required_terms)))} contract checks")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
