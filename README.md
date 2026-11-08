# A Reinforcement Learning Agent for Human Behavior-Aware Search and Rescue in Dynamic Fire Environments

## 1. Project Overview

This project develops an autonomous UAV-based search and rescue system for indoor fire emergencies using Reinforcement Learning.

The system is developed in Unity 6 and uses the Unity ML-Agents framework to train and evaluate autonomous rescue agents in a dynamic indoor fire environment.

The UAV is designed to explore rooms, detect victims, avoid obstacles and dangerous fire zones, pick up victims, and deliver them to safe exits.

The project focuses on human behavior-aware rescue decision-making under partially observable environmental conditions.

## 2. Main Objectives

- Develop a dynamic indoor fire rescue simulation.
- Model victims with different behavioural states.
- Enable autonomous UAV navigation and victim rescue.
- Investigate reinforcement learning for rescue decision-making.
- Compare Standard PPO and Recurrent PPO.
- Evaluate rescue performance under changing fire, smoke and victim movement conditions.

## 3. Technologies Used

| Technology | Purpose |
|---|---|
| Unity 6 | 3D simulation environment |
| C# | Simulation and agent implementation |
| Unity ML-Agents | Reinforcement learning integration |
| Python | Training and evaluation |
| PyTorch | Neural network training |
| PPO | Reinforcement learning algorithm |
| Recurrent PPO | PPO with LSTM-based memory |
| TensorBoard | Training monitoring |
| Matplotlib | Result visualization |

## 4. System Architecture

The project consists of the following main components:

### Environment Simulation
- Indoor building layout.
- Multiple rooms and corridors.
- Fire propagation.
- Smoke simulation.
- Obstacles and rescue exits.

### Victim Behaviour Modelling
Victims are represented using behavioural states such as:
- Calm
- Panic
- Flee
- Hide
- Trapped
- Follow
- Rescued

### UAV Agent
The autonomous UAV:
- Observes its surroundings.
- Detects victims and environmental hazards.
- Navigates through rooms.
- Avoids obstacles.
- Picks up victims.
- Delivers victims to rescue exits.

### Reinforcement Learning
The project investigates:
- Heuristic BFS
- Standard PPO
- Recurrent PPO (LSTM-based)

## 5. Observation and Action Space

The agent uses a 161-dimensional observation space.

The observations include:
- Local obstacle information.
- Fire intensity.
- Victim and exit information.
- UAV position.
- Remaining victims.
- Active fire information.
- Remaining mission time.
- Payload status.
- Smoke density.
- Victim behavioural state.
- Victim velocity.
- Victim threat level.
- Relative victim direction.

The discrete action space contains five actions:

| Action | Description |
|---|---|
| 0 | Move North |
| 1 | Move South |
| 2 | Move West |
| 3 | Move East |
| 4 | Grab / Hover |

## 6. Reward Function

The reward system encourages successful rescue and safe navigation.

The implemented reward components include:

- Victim pickup reward.
- Victim delivery reward.
- Mission completion reward.
- Exploration reward.
- New-room discovery reward.
- Victim detection reward.
- Distance-based progress reward.
- Collision penalty.
- Fire-risk penalty.
- Time penalty.
- Unnecessary hovering penalty.

Refer to `RewardConfig.cs` and `DroneAgent.cs` for the reward implementation.

## 7. Algorithms

### Heuristic BFS
A rule-based navigation baseline that uses room connectivity and pathfinding to reach victims and exits.

### Standard PPO
A feedforward Proximal Policy Optimization agent trained using environmental observations and reward feedback.

### Recurrent PPO
A PPO agent with LSTM-based memory. It uses previous information to support decision-making under partial observability.

## 8. Project Structure

```text
FireRescueAI/
│
├── Assets/
│   └── FireRescueAI/
│       ├── Scenes/
│       ├── Scripts/
│       ├── Materials/
│       └── Prefabs/
│
├── Packages/
├── ProjectSettings/
│
├── results/
├── train_standard_ppo.bat
├── train_recurrent_ppo.bat
├── view_training.bat
├── README.md
└── .gitignore
```

The exact folders and scripts may vary depending on the current project version.

## 9. Dependencies

### Software Requirements

- Unity 6
- Unity ML-Agents package compatible with the project
- Python environment compatible with the installed ML-Agents version
- PyTorch
- TensorBoard
- NumPy
- Pandas
- Matplotlib

### Python Setup

Create and activate a Python virtual environment:

```bash
python -m venv venv_firerescue
```

Windows:

```bash
venv_firerescue\Scripts\activate
```

Install the compatible ML-Agents and supporting packages.

Check the required versions against the project's Unity ML-Agents package before installation.

For example:

```bash
python -m pip install mlagents
python -m pip install tensorboard pandas matplotlib
```

Note: The exact Python, PyTorch and ML-Agents versions must be compatible with the Unity package version used by this project.

## 10. Running the Unity Simulation

1. Install Unity Hub.
2. Install the Unity version required by the project.
3. Clone or download this repository.
4. Open Unity Hub.
5. Select Add Project from Disk.
6. Select the FireRescueAI project folder.
7. Open the project.
8. Open the main simulation scene:

```text
Assets/FireRescueAI/Scenes/FireRescue_Main.unity
```

9. Press Play to start the simulation.

## 11. Control Modes

The project supports the following control modes:

| Key | Mode |
|---|---|
| 1 | Manual Control |
| 2 | Heuristic Autonomous |
| 3 | Standard PPO |
| 4 | Recurrent PPO |

The availability of trained-model inference depends on the corresponding model being present and correctly configured.

## 12. Training the Reinforcement Learning Agents

### Standard PPO

Run:

```text
train_standard_ppo.bat
```

### Recurrent PPO

Run:

```text
train_recurrent_ppo.bat
```

Start the training process and ensure that the Unity environment connects successfully to the Python training process.

The training configuration files are stored in the project's training configuration directory.

The configured maximum training duration is 100,000 environment steps per algorithm, subject to the training configuration.

## 13. Monitoring Training

TensorBoard can be used to monitor training progress.

Activate the Python virtual environment and run:

```bash
python -m tensorboard.main --logdir results
```

Open the TensorBoard address displayed in the terminal.

## 14. Evaluation

The project evaluates the rescue agents using performance measures such as:

- Rescue success rate.
- Mean victims rescued.
- Average rescue time.
- Mean collisions per episode.
- Path flight efficiency.
- Cumulative reward.
- Convergence speed.

Evaluation results should be generated from completed simulation runs and stored in the results directory.

## 15. Research Focus

The main research focus is to investigate whether recurrent memory can improve UAV rescue decision-making in dynamic indoor fire environments with limited visibility and changing victim behaviour.

The comparison between Standard PPO and Recurrent PPO helps examine the usefulness of memory-based decision-making.

## 16. Future Enhancements

- More realistic smoke and fire physics.
- Improved victim movement modelling.
- Better victim trajectory prediction.
- Improved navigation in complex indoor environments.
- Extended evaluation across multiple environmental scenarios.

## 17. Authors

B.Tech Project – Group 22

Amrita School of Computing

## 18. Disclaimer

This project is an academic research simulation. It is not intended to control real-world UAVs or replace professional emergency response systems.
