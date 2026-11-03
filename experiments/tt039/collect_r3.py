"""Fixed TT-039 R3 collection adapter; shared R2 runtime is a read-only input."""
import hashlib
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile

REPO=Path(__file__).resolve().parents[2]
ROOT=REPO/"artifacts/tt039-r3"


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def verify_files(repo,root,binding,mandatory):
    missing=set(mandatory)-binding["files"].keys()
    if missing:raise ValueError("FreezeInputs: missing "+", ".join(sorted(missing)))
    if not binding["sourceFiles"]:raise ValueError("FreezeSource: empty source binding")
    for name,expected in binding["sourceFiles"].items():
        path=Path(repo)/name
        if not path.is_file():raise ValueError("FreezeMissing: source "+name)
        if digest(path)!=expected:raise ValueError("FreezeSource: changed "+name)
    for name,expected in binding["files"].items():
        path=Path(root)/name
        if not path.is_file():raise ValueError("FreezeMissing: "+name)
        if digest(path)!=expected:raise ValueError("FreezeData: changed "+name)


def extract_training(root,archive,manifest):
    root=Path(root)
    if digest(archive)!=manifest["archiveSha256"]:raise ValueError("TrainingArchive: changed archive")
    sys.path.insert(0,str(REPO/"benchmarks/scheduler-simulator"))
    from revised import check_contract
    with tarfile.open(archive) as source:
        payload=source.extractfile("contract.json").read()
        if hashlib.sha256(payload).hexdigest()!=manifest["files"]["contract.json"]:
            raise ValueError("TrainingArchive: contract hash")
        contract=json.loads(payload);check_contract(contract)
        names=["contract.json","source-identity.json","runtime-identity.json","image.json"]
        names += [r["name"]+"/"+name for r in contract["cases"] for name in ("report.json","process.json")]
        destination=root/"training";destination.mkdir(parents=True,exist_ok=False)
        identities={}
        for name in sorted(names):
            if Path(name).is_absolute() or ".." in Path(name).parts:raise ValueError("TrainingArchive: relative input path required")
            member=source.getmember(name)
            if not member.isfile():raise ValueError("TrainingArchive: regular JSON input required")
            data=source.extractfile(member).read();actual=hashlib.sha256(data).hexdigest()
            if actual!=manifest["files"][name]:raise ValueError("TrainingArchive: input hash "+name)
            target=destination/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
            identities[name]=actual
    return dict(archiveSha256=manifest["archiveSha256"],sourceFreeze=manifest["sourceFreeze"],
                forecastFreeze=manifest["forecastFreeze"],files=identities,
                role='Every R2 run is observed R3 training; original historical labels remain unchanged')


def native_driver(root):
    spec=importlib.util.spec_from_file_location("tt039_native_driver",Path(__file__).with_name("collect.py"))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    # One serialized producer: only its output root changes. No policy/split
    # implementation is replaced and no existing module/source is modified.
    module.ROOT=Path(root)
    return module


def prepare(root=ROOT):
    root=Path(root);root.mkdir(parents=True,exist_ok=True)
    evidence=REPO/"docs/issues/evidence/2026-11-03/tt039-r2"
    manifest=json.loads((evidence/"evidence-manifest.json").read_text())
    provenance=extract_training(root,evidence/"raw-evidence.tar.gz",manifest)
    (root/"training-provenance.json").write_text(json.dumps(provenance,indent=2,sort_keys=True)+"\n")
    for name in ("runtime-identity.json","image.json"):
        shutil.copyfile(root/"training"/name,root/name)
    shutil.copyfile(REPO/"experiments/tt039/contract-r3.json",root/"contract.json")
    contract=json.loads((root/"contract.json").read_text())
    if contract["trainingArchiveSha256"]!=provenance["archiveSha256"]:raise ValueError("TrainingArchive: declared source differs")
    runtime=REPO/"artifacts/tt039-r2/runtime"
    driver=native_driver(root)
    if driver.identity(runtime)!=json.loads((root/"runtime-identity.json").read_text()):
        raise ValueError("NativeIdentity: pinned R2 executable unavailable or changed")
    (root/"runtime").symlink_to(runtime,target_is_directory=True)
    paths=[p for p in (REPO/"src/TurboTest.EFCore").rglob("*.cs") if not {"bin","obj"}&set(p.parts)]
    paths += list(Path(__file__).parent.glob("*.py"))+list(Path(__file__).parent.glob("*.cs"))+list(Path(__file__).parent.glob("*.csproj"))
    paths += [Path(__file__).with_name(n) for n in ("contract.json","contract-r3.json","packages.lock.json")]
    paths += [REPO/n for n in ("Directory.Build.props","global.json","scripts/verify.py",
        "benchmarks/TurboTest.Benchmarks/BenchmarkWorkload.cs","benchmarks/TurboTest.Benchmarks/WorkloadMix.cs")]
    paths += [REPO/"benchmarks/scheduler-simulator"/n for n in
        ("model.py","reference.py","revised.py","trajectory.py","protocol-r2.md","protocol-r3.md")]
    identities={p.relative_to(REPO).as_posix():digest(p) for p in sorted(set(paths))}
    (root/"source-identity.json").write_text(json.dumps(identities,indent=2,sort_keys=True)+"\n")
    for path in identities:
        target=root/"source-snapshots"/path;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(REPO/path,target)
    print("Prepared all 40 historical training runs; shared native runtime is a pinned read-only input.",flush=True)


def committed_binding(root,local_name,tracked_name,key,repo=REPO):
    path=Path(root)/local_name
    if not path.is_file():raise ValueError("FreezeMissing: "+local_name)
    frozen=json.loads(path.read_text());checkpoint=frozen[key]
    committed=json.loads(subprocess.check_output(["git","show",checkpoint+":"+tracked_name],cwd=repo,text=True))
    if committed!={k:v for k,v in frozen.items() if k!=key}:raise ValueError("FreezeRecord: committed binding differs")
    if subprocess.run(["git","merge-base","--is-ancestor",checkpoint,"HEAD"],cwd=repo).returncode:
        raise ValueError("FreezeRecord: checkpoint is not an ancestor")
    return frozen


def check_freeze(root=ROOT):
    root=Path(root)
    binding=committed_binding(root,"collection-freeze.json","experiments/tt039/collection-r3-binding.json","sourceCommit")
    common={"contract.json","source-identity.json","runtime-identity.json","image.json","training-provenance.json","qualification.json"}
    verify_files(REPO,root,binding,common)
    receipt=json.loads((root/"qualification.json").read_text())
    if receipt["exitCode"]!=0 or not receipt["processGroupStopped"] or receipt["deadlineExceeded"]:
        raise ValueError("FreezeQualification: native routing smokes did not settle")
    forecast=committed_binding(root,"forecast-freeze.json","benchmarks/scheduler-simulator/forecast-r3-binding.json","checkpoint")
    verify_files(REPO,root,forecast,common|{"fit.json","forecast.json","collection-freeze.json"})
    fitted=json.loads((root/"fit.json").read_text())
    verify_files(REPO,root,forecast,common|{"fit.json","forecast.json","collection-freeze.json"}|set(fitted["trainingFiles"]))
    driver=native_driver(root)
    if driver.identity(root/"runtime")!=json.loads((root/"runtime-identity.json").read_text()):
        raise ValueError("NativeIdentity: shared runtime changed")


def main():
    action=sys.argv[1]
    if action=="prepare":prepare();return
    driver=native_driver(ROOT)
    if action=="qualify":
        for mode in ("awaited","background"):
            driver.run_case("qualified-smoke-"+mode,mode,0,16,8)
    elif action=="validate":
        check_freeze()
        contract=json.loads((ROOT/"contract.json").read_text())
        for row in contract["cases"]:
            if row["split"]=="validation":
                driver.run_case(row["name"],row["mode"],row["waitMilliseconds"],contract["cycles"],contract["warmups"])
    else:raise ValueError("CollectionAction: prepare, qualify or validate required")


if __name__=="__main__":
    main()
