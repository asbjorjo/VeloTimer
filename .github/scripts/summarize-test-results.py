from collections import Counter, defaultdict
from datetime import datetime
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
durations = []
class_spans = {}
SLOWEST_TEST_COUNT = 15


def timestamp(value):
    # TRX timestamps carry 7 fractional digits, more than fromisoformat accepts before Python 3.11.
    if not value:
        return None
    try:
        head, _, rest = value.partition(".")
        digits = "".join(c for c in rest if c.isdigit())
        zone = rest[len(digits):]
        return datetime.fromisoformat(f"{head}.{digits[:6].ljust(6, '0')}{zone}" if rest else value)
    except ValueError:
        return None


def seconds(duration):
    # TRX durations look like "00:01:02.3456789".
    try:
        hours, minutes, rest = duration.split(":")
        return int(hours) * 3600 + int(minutes) * 60 + float(rest)
    except (AttributeError, ValueError):
        return 0.0

for result_file in result_files:
    project = result_file.parent.parent.name
    root = ElementTree.parse(result_file).getroot()
    for result in root.iter():
        if local_name(result.tag) != "UnitTestResult":
            continue

        outcome = result.get("outcome", "").casefold()
        name = result.get("testName", "(unnamed test)")
        durations.append((seconds(result.get("duration")), project, name))
        start, end = timestamp(result.get("startTime")), timestamp(result.get("endTime"))
        if start and end:
            # Wall-clock span of a test class, from its first test's start to its last test's end.
            # Unlike test durations, this includes class fixture setup and waits between tests.
            key = (project, name.split("(", 1)[0].rsplit(".", 1)[0])
            first, last = class_spans.get(key, (start, end))
            class_spans[key] = (min(first, start), max(last, end))
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

lines.extend(["", f"### Slowest tests (top {SLOWEST_TEST_COUNT})", "",
              "| Seconds | Project | Test |", "| ---: | --- | --- |"])
lines.extend(f"| {elapsed:.1f} | {markdown_cell(project)} | {markdown_cell(name)} |"
             for elapsed, project, name in sorted(durations, reverse=True)[:SLOWEST_TEST_COUNT])

lines.extend(["", f"### Longest test class spans (top {SLOWEST_TEST_COUNT})", "",
              "| Seconds | Project | Class |", "| ---: | --- | --- |"])
spans = sorted(((last - first).total_seconds(), project, name) for (project, name), (first, last) in class_spans.items())
lines.extend(f"| {elapsed:.1f} | {markdown_cell(project)} | {markdown_cell(name)} |"
             for elapsed, project, name in reversed(spans[-SLOWEST_TEST_COUNT:]))

summary = "\n".join(lines) + "\n"
summary_path = os.environ.get("GITHUB_STEP_SUMMARY")
if summary_path:
    with open(summary_path, "a", encoding="utf-8") as output:
        output.write(summary)
# Also print to the log, which stays readable where the job summary is not.
print(summary, end="")
