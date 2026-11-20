from collections import Counter, defaultdict
from pathlib import Path
from xml.etree import ElementTree
import os


def markdown_cell(value):
    return value.replace("|", "\\|").replace("`", "\\`").replace("\r", " ").replace("\n", " ")


def local_name(tag):
    return tag.rsplit("}", 1)[-1]


result_files = sorted(Path("src").glob("*.Test/TestResults/*.trx"))
counts = defaultdict(Counter)
failed_tests = []

for result_file in result_files:
    project = result_file.parent.parent.name
    root = ElementTree.parse(result_file).getroot()
    for result in root.iter():
        if local_name(result.tag) != "UnitTestResult":
            continue

        outcome = result.get("outcome", "").casefold()
        if outcome == "passed":
            counts[project]["passed"] += 1
        elif outcome in {"failed", "error", "timeout", "aborted", "disconnected"}:
            counts[project]["failed"] += 1
            failed_tests.append((project, result.get("testName", "(unnamed test)")))
        elif outcome in {"notexecuted", "notrunnable", "skipped", "inconclusive"}:
            counts[project]["skipped"] += 1
        else:
            counts[project]["other"] += 1


def row(name, values):
    passed, failed, skipped, other = (values[key] for key in ("passed", "failed", "skipped", "other"))
    return f"| {markdown_cell(name)} | {passed} | {failed} | {skipped} | {other} | {passed + failed + skipped + other} |"


total = sum(counts.values(), Counter())
lines = [
    "## Test summary",
    "",
    "| Project | Passed | Failed | Skipped | Other | Total |",
    "| --- | ---: | ---: | ---: | ---: | ---: |",
    row("Overall", total),
]
lines.extend(row(project, counts[project]) for project in sorted(counts))

if not result_files:
    lines.extend(["", "No TRX result files were found."])

lines.extend(["", "### Failed tests"])
if failed_tests:
    lines.extend(f"- {markdown_cell(project)}: {markdown_cell(name)}" for project, name in failed_tests)
else:
    lines.append("None")

summary = "\n".join(lines) + "\n"
summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
if summary_path:
    with open(summary_path, "a", encoding="utf-8") as output:
        output.write(summary)
else:
    print(summary, end="")
