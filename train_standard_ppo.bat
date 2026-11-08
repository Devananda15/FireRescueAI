@echo off
setlocal
echo ==========================================================
echo  FireRescue AI -- Training: STANDARD PPO (Feedforward MLP)
echo ==========================================================
echo.
echo  Behavior:   DroneRescue
echo  Network:    256-unit MLP x2 layers (no memory)
echo  Max steps:  100,000
echo.

if not exist venv_firerescue\Scripts\activate.bat (
    echo [ERROR] venv_firerescue not found. Please run setup_training.bat first.
    pause
    exit /b 1
)

call venv_firerescue\Scripts\activate.bat

for /f "tokens=2 delims==" %%I in ('wmic os get localdatetime /value 2^>nul') do set dt=%%I
if "%dt%"=="" set dt=%date:~-4%%date:~4,2%%date:~7,2%_%time:~0,2%%time:~3,2%%time:~6,2%
set RUN_ID=StandardPPO_%dt:~0,8%_%dt:~8,6%

echo  Run ID: %RUN_ID%
echo  1. Open Unity and press PLAY on FireRescue_Main
echo  2. ML-Agents will connect and train the drone autonomously
echo.
echo  Press Ctrl+C to stop training at any time.
echo.

call mlagents-learn config/standard_ppo.yaml --run-id=%RUN_ID% --force
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Training interrupted or crashed with exit code %ERRORLEVEL%.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo  Training complete! Results and .onnx model saved to: results\%RUN_ID%
echo  Run view_training.bat to inspect training curves in TensorBoard.
pause
