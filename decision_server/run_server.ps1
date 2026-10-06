# Start the decision server on http://127.0.0.1:8765 (Unity's HttpModelSelector expects this address).
# First run downloads the pinned Laya and GLiClass checkpoints from Hugging Face.
#   Optional environment variables:
#     DECISION_DEVICE=cpu       force CPU (default: cuda if available)
#     DECISION_MODELS=laya      load only some models (default: laya,gliclass)
#     LAYA_ROTATE=0             ask Laya's choice once instead of averaging over option rotations
Set-Location $PSScriptRoot
& .\.venv\Scripts\python.exe -m uvicorn server:app --host 127.0.0.1 --port 8765
