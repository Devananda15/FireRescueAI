@echo off
setlocal
echo ==========================================================
echo  FireRescue AI -- TensorBoard Visualisation
echo ==========================================================
echo.
echo  Opening TensorBoard dashboard...
echo  Dashboard URL:  http://localhost:6006
echo.

if not exist venv_firerescue\Scripts\activate.bat (
    echo [ERROR] venv_firerescue not found. Please run setup_training.bat first.
    pause
    exit /b 1
)

call venv_firerescue\Scripts\activate.bat

if not exist results (
    mkdir results
)

echo Starting TensorBoard at http://localhost:6006 ...
echo Press Ctrl+C to close TensorBoard when done.
echo.
python -m tensorboard.main --logdir results --port 6006
pause
