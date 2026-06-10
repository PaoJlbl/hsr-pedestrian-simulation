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
python .\analysis\crowd_bottleneck_visualization.py --input .\unity\crowd_state_Burst.csv --output .\analysis\crowd_analysis_burst_output
python .\analysis\control_effect_visualization.py --input-dir .\unity\ControlExport --output-dir .\analysis\control_analysis_output
```

## Git LFS 使用

本仓库使用 Git LFS 管理 FBX、ONNX、PNG、PDF 等较大二进制文件。首次克隆后请执行：

```powershell
git lfs install
git lfs pull
```

如果未安装 Git LFS，Unity 场景中的模型或推理网络可能只显示为 LFS 指针文件，无法正常加载。

## 复现实验流程

1. 克隆仓库并拉取 LFS 资产。
2. 使用 Unity Hub 打开 `unity/`，Unity 版本建议为 `2022.3.62f3c1`。
3. 打开 `Scene_FBX_CrowdInference.unity` 或 `Scene_FBX_BurstControl.unity`，检查预制体中的 Behavior Parameters 是否引用已保留的 ONNX 模型。
4. 运行场景，生成或更新 `crowd_state_normal.csv`、`crowd_state_Burst.csv`、`ControlExport/` 指标文件。
5. 使用 `analysis/` 下脚本生成瓶颈识别图表、管控对比图和报告。

## 注意事项

- 本仓库是毕业设计归档版，保留复现实验所需的核心资产，不包含 Unity `Library/`、`Logs/`、`UserSettings/` 等本地生成目录。
- 已排除训练中间 `.pt` 权重、TensorBoard 事件日志和未被核心场景引用的大型 FBX 文件。
- 如果需要重新训练 ML-Agents，请在本地重新生成训练结果，并按需选择最终 ONNX 模型纳入版本管理。

