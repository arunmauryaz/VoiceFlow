"""Run with the installed Bark Python runtime; no network or real model required."""
import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "VoiceFlow" / "LocalRuntime"))
import torch
import bark_loader


class BarkCacheTests(unittest.TestCase):
    def test_cache_reuses_exact_weights_without_decoding(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "model.bark"
            source.write_bytes(b"test")
            weights = {"weight": torch.tensor([[1.0, 2.0], [3.0, 4.0]])}
            with patch.object(bark_loader, "load_state_dict", return_value=(weights, {"test": 1})) as decode:
                bark_loader._load_decoded(source)
                loaded, cfg = bark_loader._load_decoded(source)
                self.assertTrue(torch.equal(weights["weight"], loaded["weight"]))
                self.assertEqual(cfg, {"test": 1})
                self.assertEqual(decode.call_count, 1)
                del loaded
                stat = source.stat()
                os.utime(source, ns=(stat.st_atime_ns, stat.st_mtime_ns + 1_000_000))
                bark_loader._load_decoded(source)
                self.assertEqual(decode.call_count, 2)

    def test_corrupt_cache_rebuilds_and_low_disk_still_loads(self):
        with tempfile.TemporaryDirectory() as root:
            source = Path(root) / "model.bark"
            source.write_bytes(b"test")
            cache = Path(str(source) + ".voiceflow-cache-v1.pt")
            cache.write_bytes(b"broken")
            weights = {"weight": torch.ones(2)}
            with patch.object(bark_loader, "load_state_dict", return_value=(weights, {})) as decode:
                result, _ = bark_loader._load_decoded(source)
                self.assertTrue(torch.equal(result["weight"], weights["weight"]))
                del result
                decode.assert_called_once()
                cache.unlink()
                with patch.object(bark_loader.shutil, "disk_usage", return_value=type("Disk", (), {"free": 0})()):
                    result, _ = bark_loader._load_decoded(source)
                    self.assertIs(result, weights)
                    self.assertFalse(cache.exists())


if __name__ == "__main__":
    unittest.main()
