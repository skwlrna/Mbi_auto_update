from pathlib import Path
import hashlib,json,shutil,sys
root=Path(sys.argv[1]).resolve(); here=Path(__file__).resolve().parent
manifest=json.loads((here/'v0175-base-hashes.json').read_text())
# Validate the entire base before making any changes.
for name,expected in manifest.items():
 p=root/name
 if expected is None:
  if p.exists(): raise SystemExit(f'Unexpected existing file: {name}')
 elif not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest()!=expected:
  raise SystemExit(f'V0.1.74 source mismatch: {name}')
for name in manifest:
 p=root/name;p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(here/'v0175-overlay'/name,p)
print('V0175 overlay applied to verified V0.1.74 base')
