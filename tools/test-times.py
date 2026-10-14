#!/usr/bin/env python3
"""Each test class's time in a run of Passenger's tests, from the run's .trx results, held to the suite's budget.

Prints every class with its tests and the seconds they took (xunit's own time for each test: its class made, the test
run and its class disposed; a class fixture's setup is outside it), the slowest first, and the run's own time from its
start to its end. Given the budget (tests/Passenger.Tests/TestBudget.json), a class over its seconds is a warning, and
the run over its minutes fails: the budget says how long a run may take before someone decides it should, and a class's
seconds say where the time went. On GitHub Actions the warnings and the failure are annotations and the table goes in
the step's summary. CI's Test step runs it after `dotnet test --logger trx`; here, after the same:
python3 -I tools/test-times.py TestResults --budget tests/Passenger.Tests/TestBudget.json
"""
import argparse
import glob
import json
import os
import sys
import xml.etree.ElementTree as ElementTree
from datetime import datetime, timedelta, timezone

TRX = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
PREFIX = "Passenger.Tests."


def when(text):
    """A .trx time, 2026-10-13T20:38:14.1234567-07:00, which Python reads only to the microsecond."""
    stamp, zone = text[:-6], text[-6:]
    if text.endswith("Z"):
        stamp, zone = text[:-1], "+00:00"
    whole, _, fraction = stamp.partition(".")
    moment = datetime.strptime(whole, "%Y-%m-%dT%H:%M:%S") + timedelta(microseconds=int((fraction + "000000")[:6]))
    sign = -1 if zone[0] == "-" else 1
    return moment.replace(tzinfo=timezone(sign * timedelta(hours=int(zone[1:3]), minutes=int(zone[4:6]))))


def seconds(duration):
    hours, minutes, rest = duration.split(":")
    return int(hours) * 3600 + int(minutes) * 60 + float(rest)


def read(paths):
    """Each class's tests, seconds and failures, and the run's seconds, from every .trx under the paths."""
    files = []
    for path in paths:
        files += sorted(glob.glob(os.path.join(path, "**", "*.trx"), recursive=True)) if os.path.isdir(path) else [path]
    if not files:
        sys.exit(f"test-times: no .trx results under {', '.join(paths)}: run dotnet test with --logger trx")
    classes = {}
    run = 0.0
    for file in files:
        root = ElementTree.parse(file).getroot()
        times = root.find(f"{TRX}Times")
        run += (when(times.get("finish")) - when(times.get("start"))).total_seconds()
        owner = {test.get("id"): test.find(f"{TRX}TestMethod").get("className") for test in root.iter(f"{TRX}UnitTest")}
        for result in root.iter(f"{TRX}UnitTestResult"):
            name = owner.get(result.get("testId"), "?")
            name = name[len(PREFIX):] if name.startswith(PREFIX) else name
            entry = classes.setdefault(name, {"tests": 0, "seconds": 0.0, "failed": 0})
            entry["tests"] += 1
            entry["seconds"] += seconds(result.get("duration", "00:00:00"))
            entry["failed"] += result.get("outcome") not in ("Passed", "NotExecuted")
    return classes, run


def main():
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("results", nargs="+", help="a .trx file, or a folder holding them")
    parser.add_argument("--budget", help="the suite's budget: tests/Passenger.Tests/TestBudget.json")
    arguments = parser.parse_args()
    classes, run = read(arguments.results)
    budget = {}
    if arguments.budget:
        with open(arguments.budget, encoding="utf-8") as file:
            budget = json.load(file)
    run_limit = budget.get("runMinutes")
    allowed = budget.get("classes", {})
    unknown = sorted(set(allowed) - set(classes))

    def limit(name):
        return allowed.get(name, budget.get("classSeconds"))

    rows = sorted(classes.items(), key=lambda item: -item[1]["seconds"])
    total = sum(entry["seconds"] for entry in classes.values())
    lines = [f"{'Class':<44} {'Tests':>5} {'Seconds':>8} {'Budget':>7}"]
    for name, entry in rows:
        given = limit(name)
        lines.append(f"{name:<44} {entry['tests']:>5} {entry['seconds']:>8.1f} {given if given is not None else '':>7}"
                     + (f"  {entry['failed']} failed" if entry["failed"] else ""))
    lines.append(f"{'Every class':<44} {sum(e['tests'] for e in classes.values()):>5} {total:>8.1f}")
    print("\n".join(lines))
    print(f"\nThe run took {run / 60:.1f} minutes" + (f", of a budget of {run_limit}" if run_limit is not None else ""))

    github = os.environ.get("GITHUB_ACTIONS") == "true"
    over = [(name, entry["seconds"], limit(name)) for name, entry in rows
            if limit(name) is not None and entry["seconds"] > limit(name)]
    for name, spent, given in over:
        message = f"{name} took {spent:.0f} s, over its budget of {given} s in TestBudget.json"
        print(f"::warning title=Test class over budget::{message}" if github else f"warning: {message}")
    for name in unknown:
        message = f"TestBudget.json gives {name} a budget, but the run has no such class"
        print(f"::warning title=Test budget::{message}" if github else f"warning: {message}")
    failed = run_limit is not None and run > run_limit * 60
    if failed:
        message = (f"The tests took {run / 60:.1f} minutes, over the run's budget of {run_limit} in TestBudget.json: "
                   "make what grew faster, or raise the budget knowing why")
        print(f"::error title=Tests over budget::{message}" if github else f"error: {message}")

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as file:
            file.write(f"### Test time on {os.environ.get('RUNNER_OS', 'this runner')}: {run / 60:.1f} minutes"
                       + (f" of {run_limit}" if run_limit is not None else "") + "\n\n")
            file.write("| Class | Tests | Seconds | Budget |\n|---|---:|---:|---:|\n")
            for name, entry in rows:
                given = limit(name)
                mark = " **over**" if given is not None and entry["seconds"] > given else ""
                file.write(f"| {name} | {entry['tests']} | {entry['seconds']:.1f} | {given if given is not None else ''}{mark} |\n")
            file.write("\n")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
