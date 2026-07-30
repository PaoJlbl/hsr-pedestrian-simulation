# 基于多智能体的高铁枢纽行人流三维动态模拟与瓶颈识别

本仓库整理了毕业设计项目的核心代码与可复现实验资产，英文仓库名为 `hsr-pedestrian-simulation`。项目面向高铁枢纽大客流场景，以深圳北站空间模型为基础，结合 Unity 3D、ML-Agents 多智能体强化学习、客流状态采样和 Python 后处理分析，实现行人流三维动态模拟、瓶颈区域识别与管控效果评估。

## 项目目标

- 构建高铁枢纽站内行人通行空间与多智能体行人仿真环境。
- 训练行人智能体在复杂站内空间中完成出入口/闸机导向移动。
- 采集仿真过程中的密度、速度、通行量、碰撞和区域压力指标。
- 基于时空网格计算瓶颈评分，识别持续拥堵和候选热点区域。
- 对比基础场景与管控场景，评估限流、导向等措施对瓶颈缓解的效果。

## 目录结构

```text
.
├── analysis/                 # Python 数据分析、可视化和论文辅助材料生成脚本
├── docs/
│   └── FILE_INVENTORY.md     # 中文文件作用清单
├── unity/                    # Unity 2022.3.62f3c1 工程核心文件
│   ├── Assets/               # 场景、脚本、模型、预制体、ML-Agents 模型
│   ├── Packages/             # Unity 包依赖清单
│   ├── ProjectSettings/      # Unity 工程设置
│   ├── ControlExport/        # 管控实验导出的指标数据
│   ├── crowd_state_Burst.csv # 突发客流场景状态采样数据
│   └── crowd_state_normal.csv# 常态客流场景状态采样数据
├── .gitattributes            # Git LFS 跟踪规则
├── .gitignore                # Unity/Python 排除规则
└── requirements.txt          # Python 分析脚本依赖
```

## 技术栈

- Unity：`2022.3.62f3c1`
- Unity ML-Agents：`com.unity.ml-agents 2.0.2`
- Unity AI Navigation：`com.unity.ai.navigation 1.1.6`
- Python：用于数据清洗、图表生成、瓶颈识别和报告辅助生成
- Python 依赖：`numpy`、`pandas`、`Pillow`、`python-docx`
- 大文件管理：Git LFS

## Unity 工程说明

Unity 工程位于 `unity/`，打开时请在 Unity Hub 中选择该目录作为项目根目录。

主要场景：

- `SceneTraining_Shenzhenbei.unity`：基于深圳北站 FBX 模型的多智能体训练场景。
- `Scene_FBX_CrowdInference.unity`：加载训练后 ONNX 模型进行客流推理仿真。
- `Scene_FBX_BurstControl.unity`：突发客流与管控策略仿真场景。
- `Scene_Training_Crowd.unity`：简化通道环境下的客流智能体训练场景。
- `FinalScene_CrowdRuntime.unity`：运行时客流生成与状态记录场景。
- `shenzhenbei_main.unity`：深圳北站主体模型和导航网格基础场景。

核心脚本：

- `PedestrianAgent.cs` / `CrowdPedestrianAgent.cs`：基础行人智能体和多行人训练逻辑。
- `FBXCrowdPedestrianAgent.cs` / `FBXRuntimePedestrianAgent.cs`：面向 FBX 站房模型的训练和运行时行人智能体。
- `CrowdTrainingManager.cs` / `FBXCrowdTrainingManager.cs`：训练智能体生成、起终点分配和统计管理。
- `FinalSceneController.cs` / `FBXFinalSceneController.cs`：运行时客流生成、智能体回收和指标统计。
- `FBXTrafficControlManager.cs`：突发客流下的管控策略控制与导出。
- `CrowdStateLogger.cs`：按时间和空间网格记录行人数量、速度和密度。
- `BottleneckZoneMonitor.cs`：监测重点区域密度、速度和瓶颈持续时间。
- `SimulationSpace.cs`：将真实站房空间映射为训练/分析使用的局部坐标系。

已保留的核心资产包括被主要场景引用的深圳北站 FBX 模型、场景 `.unity` 文件、预制体、配置文件，以及两个最终推理模型：

- `Assets/results/crowd_fbx_full_v1/PedestrianCrowdFBX.onnx`
- `Assets/results/crowd_stable_60agents_v1/PedestrianCrowd.onnx`

训练中间权重、TensorBoard 事件文件、运行缓存和未被场景引用的大型 FBX 已从仓库中排除，以减少仓库体积并避免上传无关生成内容。

## Python 分析说明

`analysis/` 保存仿真结果后处理和论文材料生成脚本：

- `crowd_bottleneck_visualization.py`：读取客流状态 CSV，计算网格密度、平均速度和瓶颈评分，输出热力图、散点图、瓶颈事件表和分析报告。
- `control_effect_visualization.py`：读取管控实验导出结果，生成 KPI 对比、碰撞对比和分区压力图。
- `control_measures_visualization.py`：汇总管控措施效果，生成图表和 Markdown 报告。
- `generate_arrival_chart.py`：生成深圳北站到达客流分布 HTML 图表。
- `generate_crowd_training_chart.py`：整理训练日志和 checkpoint 信息，生成训练曲线展示页。
- `build_model_explanation_docx.py`：生成模型说明 Word 文档。
- `rewrite_chapter4_stg_cbi.py`：辅助改写论文第四章相关内容。

安装依赖：

```powershell
cd "D:\AAA Learning\hsr-pedestrian-simulation"
python -m pip install -r requirements.txt
```

示例运行：

```powershell
python .\analysis\crowd_bottleneck_visualization.py --input .\unity\crowd_state_normal.csv --output .\analysis\crowd_analysis_normal_output
python .\analysis\crowd_bottleneck_visualization.py --csv .\unity\crowd_state_Burst.csv --output .\analysis\crowd_analysis_burst_output
python .\analysis\control_effect_visualization.py --input .\unity\ControlExport --output .\analysis\control_analysis_output
```

## Git LFS 使用

本仓库使用 Git LFS 管理 FBX、ONNX、PNG、PDF 等较大二进制文件。首次克隆后请执行：

```powershell
git lfs install
git lfs pull
```

如果未安装 Git LFS，Unity 场景中的模型或推理网络可能只显示为 LFS 指针文件，无法正常加载。

## 大客流场景 Demo

该 Demo 直接使用仓库中已有的 ONNX 模型进行推理，不需要重新训练 ML-Agents。推荐先运行无管控场景确认模型和站房环境正常，再运行管控场景进行对比。

### 1. 拉取完整模型资产

克隆仓库后进入项目根目录，确认 Git LFS 已安装并拉取大文件：

```powershell
git lfs install
git lfs pull
git lfs ls-files
```

确认以下两个文件是实际的 ONNX 二进制文件，而不是只有几行文本的 LFS 指针：

- `unity/Assets/results/crowd_fbx_full_v1/PedestrianCrowdFBX.onnx`：深圳北站 FBX 大客流场景使用的模型。
- `unity/Assets/results/crowd_stable_60agents_v1/PedestrianCrowd.onnx`：简化通道/基础运行场景使用的模型。

### 2. 打开 Unity 工程

1. 在 Unity Hub 中选择 `unity/` 作为项目目录。
2. 使用 Unity `2022.3.62f3c1` 打开工程，等待首次导入 FBX、ONNX 和包依赖完成。
3. 如果 Package Manager 提示恢复依赖，允许它根据 `Packages/manifest.json` 安装 ML-Agents `2.0.2` 和 AI Navigation `1.1.6`。
4. 等待 Console 不再出现编译错误后再进入 Play Mode。`EditorBuildSettings` 当前没有预设场景列表，因此需要从 Project 窗口手动打开下述场景。

### 3. 核验推理模型绑定

在 Project 窗口打开 `Assets/prefabs/Pedestrian_CrowdFBXInference.prefab`，检查其 `Behavior Parameters`：

- `Model` 指向 `PedestrianCrowdFBX.onnx`。
- `Behavior Name` 为 `PedestrianCrowdFBX`。
- `Behavior Type` 为 `Inference Only`。
- `Inference Device` 保持 `Default`；如显卡推理出现兼容问题，可改为 CPU 后重试。

基础运行预制体 `Assets/prefabs/Pedestrians_CrowdRuntime.prefab` 应绑定 `PedestrianCrowd.onnx`，其 `Behavior Name` 为 `PedestrianCrowd`、`Behavior Type` 同样为 `Inference Only`。如果 Inspector 中 `Model` 显示为 `None`，请从上述路径重新拖入对应 ONNX；不要将 Behavior Type 改为训练模式。

### 4. 运行无管控大客流推理

1. 双击打开 `Assets/Scenes/Scene_FBX_CrowdInference.unity`。
2. 在 Hierarchy 中选中 `FBXFinalSceneController`，确认 `Agent Prefab` 引用 FBX 推理行人预制体，`Simulation Frame`、出生区域和东西侧出口均未丢失。
3. 将场景设为 `Burst`。当前大客流默认参数为：出生区域索引 `1/3/5/7`、对应客流量 `1104/1295/1437/863`、到达持续时间 `600 s`、最大同时活动人数 `550`、对象池大小 `650`。
4. 如果 `Auto Start On Play` 已勾选，点击 Unity 顶部 Play 即可；如果未勾选，进入 Play Mode 后使用左上角运行时 GUI 启动 `Burst` 场景。
5. 观察行人是否从多个出生区域生成、沿站房空间移动并到达东西侧出口。运行时 GUI 会显示目标人数、已生成/到达/失败人数、当前活动人数、峰值人数、平均通行时间、出口分流和碰撞等指标。
6. `CrowdStateLogger` 默认每 `0.5 s` 按 `30 x 3` 网格采样人数、密度和速度。该场景当前输出文件名为 `crowd_state_Burst.csv`，文件写入 `unity/` 项目根目录；每次重新进入 Play Mode 会覆盖同名文件。

如果电脑无法流畅维持 550 个智能体，可先将 `Burst Max Active Agents` 调低到 `100-250` 验证流程；这只降低同时在场人数，不改变已有 ONNX 的推理方式。

### 5. 运行大客流分级管控推理

1. 停止前一个场景，双击打开 `Assets/Scenes/Scene_FBX_BurstControl.unity`。
2. 再次确认 `FBXFinalSceneController` 使用相同的 `PedestrianCrowdFBX.onnx` 推理预制体，确保对比实验只改变管控策略。
3. 在 Hierarchy 中选中 `FBXTrafficControlManager`：
   - `Enable Control` 关闭时运行无管控基线；当前场景默认即为关闭，实验名为 `Burst_Base`。
   - `Enable Control` 打开后启用分级管控；可分别开启 `Use Spawn Metering`、`Use Gate Guidance` 和 `Use Secondary Mild Guidance`。
   - 默认限流区域为主区域 `1/3`、次区域 `5`；拥堵升级持续时间为 `8 s`。
   - Level 1/2/3 的主区域放行倍率依次为 `0.70/0.50/0.35`，并逐级增加出口引导惩罚。
4. 为避免覆盖和混淆结果，每组实验修改 `Experiment Name`，例如 `Burst_Base`、`Burst_MeteringOnly`、`Burst_Metering_GateGuidance`。
5. 点击 Play 并启动 `Burst`。观察 `Current Level`、限流/导向激活状态、干预次数、等级切换次数和累计管控时间，并与无管控场景的峰值密度、碰撞和通行时间比较。
6. 场景完成后，`Export On Scenario Finished` 会把汇总指标和分区指标写入 `unity/ControlExport/`。如需提前结束演示，应先记录屏幕指标；自动 CSV 导出以场景正常完成为准。

### 6. 生成瓶颈识别与管控对比结果

退出 Play Mode 后，在仓库根目录运行：

```powershell
python -m pip install -r requirements.txt
python .\analysis\crowd_bottleneck_visualization.py --input .\unity\crowd_state_Burst.csv --output .\analysis\crowd_analysis_burst_output
python .\analysis\control_effect_visualization.py --input-dir .\unity\ControlExport --output-dir .\analysis\control_analysis_output
```

瓶颈分析结果位于 `analysis/crowd_analysis_burst_output/`，包括密度/速度热力图、瓶颈评分、热点事件和 Markdown 报告；管控方案对比结果位于 `analysis/control_analysis_output/`。

### 常见问题

- 行人不生成：检查 `FBXFinalSceneController` 的 `Auto Start On Play`，或在运行时 GUI 中手动启动 `Burst`。
- 行人原地不动：优先检查推理预制体的 ONNX、`Behavior Type = Inference Only`、Behavior Name，以及场景中的出口和 Simulation Frame 引用。
- 模型显示丢失：执行 `git lfs pull`，确认 ONNX 文件不是 LFS 指针，再重新打开 Unity。
- 大量行人越界或穿模：确认场景 FBX、NavMesh/碰撞体、预制体和 `.meta` 文件均完整导入，且没有修改 Simulation Space 的空间映射。
- CSV 没有生成：确认已进入 Play Mode，`CrowdStateLogger` 组件启用，并检查文件是否写入 `unity/` 而不是 `Assets/`。

## 注意事项

- 本仓库是毕业设计归档版，保留复现实验所需的核心资产，不包含 Unity `Library/`、`Logs/`、`UserSettings/` 等本地生成目录。
- 已排除训练中间 `.pt` 权重、TensorBoard 事件日志和未被核心场景引用的大型 FBX 文件。
- 如果需要重新训练 ML-Agents，请在本地重新生成训练结果，并按需选择最终 ONNX 模型纳入版本管理。
