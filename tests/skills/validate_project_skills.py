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
    reference_relative = contract.get(
        "reference",
        "references/delivery-map.md",
    )
    reference_path = skill_dir / reference_relative

    for path in (skill_path, metadata_path, reference_path):
        if not path.is_file():
            errors.append(f"Missing required file: {path.relative_to(ROOT).as_posix()}")

    if errors:
        return errors

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
    if f"({reference_relative})" not in skill_text:
        errors.append(f"SKILL.md must link directly to {reference_relative}.")
    if f"${skill_name}" not in metadata_text:
        errors.append(f"agents/openai.yaml must mention ${skill_name} in default_prompt.")

    required_terms = list(contract["required_terms"])
    for scenario in contract["scenarios"]:
        required_terms.extend(scenario["required_terms"])
    for term in dict.fromkeys(required_terms):
        if term not in combined_text:
            errors.append(f"Missing contract term: {term}")

    # 每个 Skill 可声明自己的结构化门禁，避免把特定领域规则硬编码进通用校验器。
    for term in contract.get("verification_terms", []):
        if term not in combined_text:
            errors.append(f"Missing verification term: {term}")

    if not errors:
        print(
            f"PASS {skill_name}: "
            f"{len(tuple(dict.fromkeys(required_terms)))} contract checks"
        )
    return errors


def main() -> int:
    contract_paths = sorted(CONTRACT_ROOT.glob("*.contract.json"))
    errors: list[str] = []
    for contract_path in contract_paths:
        errors.extend(validate_contract(contract_path))

    if errors:
        print("\n".join(errors), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
