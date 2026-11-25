"""Behavior checks for the visual-check skill, without starting .NET, a UI or a network request.

Run from the repository root: python -B -I -m unittest discover -s tools/skill_checks -v
Requires Pillow, the dependency used by png_diff.py itself.
"""
import importlib.util
import subprocess
import tempfile
import unittest
from contextlib import redirect_stdout
from io import StringIO
from pathlib import Path
from unittest.mock import patch

from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / ".claude/skills/hearthpulse-visual-check/scripts"


def load(name):
    spec = importlib.util.spec_from_file_location(name, SCRIPTS / f"{name}.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


png_diff = load("png_diff")
render = load("render")
QUEUED = [("create", "Hearthpulse.LocalCheck"), ("wait", 0)]
RELEASED = [("release",), ("close",)]


class FakeKernel32:
    """The four mutex calls of local_check_gate, logged; WaitForSingleObject answers from `waits`."""

    def __init__(self, log, waits=(0x0,)):
        self.log, self.waits = log, list(waits)

    def CreateMutexW(self, security, owned, name):
        self.log.append(("create", name))
        return 7

    def WaitForSingleObject(self, handle, milliseconds):
        self.log.append(("wait", milliseconds))
        return self.waits.pop(0)

    def ReleaseMutex(self, handle):
        self.log.append(("release",))
        return 1

    def CloseHandle(self, handle):
        self.log.append(("close",))
        return 1


class VisualToolsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="hp-skill-test-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def compare(self, before, after):
        before.save(self.root / "before.png")
        after.save(self.root / "after.png")
        return png_diff.compare(self.root / "before.png", self.root / "after.png", self.root / "diff")

    def test_every_rgb_channel_detects_a_large_change(self):
        for channel in range(3):
            with self.subTest(channel=channel):
                changed = [0, 0, 0]
                changed[channel] = 200
                self.assertEqual("changed 100.0%", self.compare(
                    Image.new("RGB", (10, 10), (0, 0, 0)), Image.new("RGB", (10, 10), tuple(changed))))

    def test_alpha_changes_are_detected_and_the_preview_sheet_is_written(self):
        self.assertEqual("changed 100.0%", self.compare(
            Image.new("RGBA", (10, 10), (0, 0, 0, 255)), Image.new("RGBA", (10, 10), (0, 0, 0, 0))))
        with Image.open(self.root / "diff/before.png") as sheet:
            self.assertEqual((50, 10), sheet.size)
            self.assertNotEqual(sheet.getpixel((0, 0)), sheet.getpixel((20, 0)))

    def test_noise_at_the_threshold_is_ignored_but_the_next_value_is_detected(self):
        before = Image.new("RGB", (2, 1), (0, 0, 0))
        after = Image.new("RGB", (2, 1), (24, 24, 24))
        self.assertEqual("same", self.compare(before, after))
        after.putpixel((0, 0), (0, 0, 25))
        self.assertEqual("changed 50.0%", self.compare(before, after))

    def test_a_size_change_is_reported_even_when_the_pixels_are_transparent(self):
        result = self.compare(Image.new("RGBA", (1, 1)), Image.new("RGBA", (2, 1)))
        self.assertIn("size 1x1 -> 2x1", result)
        self.assertTrue((self.root / "diff/before.png").is_file())

    def run_render(self, scenes, code=0, fail_restore=False):
        calls = []
        self.gate_log = []

        def run(command, **kwargs):
            calls.append(command)
            self.gate_log.append(command[1])
            if fail_restore and command[1] == "restore":
                raise subprocess.CalledProcessError(1, command)
            if command[1] == "run":
                staging = Path(command[-1])
                self.assertEqual([], list(staging.iterdir()))
                for name in scenes:
                    Image.new("RGB", (2, 2), (20, 30, 40)).save(staging / name)
                return subprocess.CompletedProcess(command, code)
            return subprocess.CompletedProcess(command, 0)

        with patch.object(render.sys, "argv", ["render.py", str(self.root / "out"), "ava"]), \
                patch.object(render, "ensure_copy"), patch.object(render.subprocess, "run", side_effect=run), \
                patch.object(render.local_gate, "kernel32", return_value=FakeKernel32(self.gate_log)), \
                redirect_stdout(StringIO()):
            result = render.main()
        self.assertEqual(["restore", "run"], [c[1] for c in calls])
        return result

    def test_the_render_holds_the_local_check_mutex_from_before_the_build_until_after_the_run(self):
        self.assertEqual(0, self.run_render(render.REQUIRED_SCENES))
        self.assertEqual([*QUEUED, "restore", "run", *RELEASED], self.gate_log)

    def test_a_failed_render_releases_the_mutex(self):
        self.assertEqual(1, self.run_render([], code=1))
        self.assertEqual([*QUEUED, "restore", "run", *RELEASED], self.gate_log)

    def test_a_failed_build_step_releases_the_mutex(self):
        with self.assertRaises(subprocess.CalledProcessError):
            self.run_render([], fail_restore=True)
        self.assertEqual([*QUEUED, "restore", *RELEASED], self.gate_log)

    def gate(self, waits):
        log, out = [], StringIO()
        with patch.object(render.local_gate, "kernel32", return_value=FakeKernel32(log, waits)), redirect_stdout(out):
            with render.local_gate.held():
                log.append("inside")
        return log, out.getvalue()

    def test_a_busy_mutex_is_waited_for_with_one_line_and_an_abandoned_one_counts_as_acquired(self):
        gate = render.local_gate
        log, printed = self.gate([gate.WAIT_TIMEOUT, gate.WAIT_OBJECT_0])
        self.assertEqual([*QUEUED, ("wait", gate.INFINITE), "inside", *RELEASED], log)
        self.assertEqual(1, len(printed.splitlines()))
        self.assertIn("Waiting", printed)
        log, printed = self.gate([gate.WAIT_TIMEOUT, gate.WAIT_ABANDONED])
        self.assertEqual([*QUEUED, ("wait", gate.INFINITE), "inside", *RELEASED], log)
        log, printed = self.gate([gate.WAIT_ABANDONED])
        self.assertEqual([*QUEUED, "inside", *RELEASED], log)
        self.assertEqual("", printed)

    def test_a_failed_wait_raises_without_releasing_a_mutex_it_does_not_own(self):
        log = []
        with patch.object(render.local_gate, "kernel32", return_value=FakeKernel32(log, [0xFFFFFFFF])):
            with self.assertRaises(OSError):
                with render.local_gate.held():
                    log.append("inside")
        self.assertEqual([*QUEUED, ("close",)], log)

    def test_the_gate_is_a_no_op_off_windows(self):
        with patch.object(render.local_gate.sys, "platform", "linux"):
            self.assertIsNone(render.local_gate.kernel32())
            with render.local_gate.held():
                pass

    def test_success_requires_current_mandatory_scenes_but_not_optional_log_scenes(self):
        self.assertEqual(0, self.run_render(render.REQUIRED_SCENES))
        self.assertEqual(render.REQUIRED_SCENES, {p.name for p in (self.root / "out/ava").glob("*.png")})

    def test_exit_zero_without_scenes_fails_and_old_scenes_cannot_mask_it(self):
        target = self.root / "out/ava"
        target.mkdir(parents=True)
        for name in render.REQUIRED_SCENES:
            Image.new("RGB", (2, 2)).save(target / name)
        self.assertEqual(1, self.run_render([]))
        self.assertEqual([], list(target.glob("*.png")))

    def test_missing_mandatory_scene_fails_but_preserves_partial_output_for_review(self):
        scenes = render.REQUIRED_SCENES - {"overlay-layout.png"}
        self.assertEqual(1, self.run_render(scenes))
        self.assertEqual(scenes, {p.name for p in (self.root / "out/ava").glob("*.png")})

    def test_nonzero_process_exit_fails_even_with_every_required_scene(self):
        self.assertEqual(1, self.run_render(render.REQUIRED_SCENES, code=1))

    def test_an_empty_file_is_not_a_rendered_scene(self):
        for name in render.REQUIRED_SCENES:
            (self.root / name).write_bytes(b"image")
        (self.root / "overlay-layout.png").write_bytes(b"")
        self.assertEqual(["overlay-layout.png"], render.missing_scenes(self.root))

    def test_publish_does_not_write_outside_the_output_directory(self):
        outside = self.root / "outside"
        outside.mkdir()
        original = outside / "old.png"
        original.write_bytes(b"keep")
        with self.assertRaises(ValueError):
            render.publish_scenes(self.root, outside, self.root / "out")
        self.assertEqual(b"keep", original.read_bytes())

    def test_data_copy_excludes_credentials_and_rejects_the_live_directory(self):
        source = self.root / "live"
        source.mkdir()
        (source / "settings.json").write_text("{}")
        (source / "account.dat").write_bytes(b"synthetic credentials")
        with patch.object(render, "real_data_dir", return_value=source), redirect_stdout(StringIO()):
            with self.assertRaises(ValueError):
                render.ensure_copy(source)
            with self.assertRaises(ValueError):
                render.ensure_copy(source / "subfolder")
            render.ensure_copy(self.root / "scratch")
        self.assertTrue((self.root / "scratch/settings.json").is_file())
        self.assertFalse((self.root / "scratch/account.dat").exists())


if __name__ == "__main__":
    unittest.main()
