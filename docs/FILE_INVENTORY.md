# 文件作用清单

本文档说明仓库中主要文件和目录的用途，便于后续复现、展示和维护。

## 根目录

- `README.md`：项目中文说明，包含研究目标、目录结构、运行方式和 Git LFS 使用说明。
- `.gitignore`：排除 Unity 缓存、Python 缓存、训练中间产物和编辑器临时文件。
- `.gitattributes`：配置 Git LFS 跟踪 FBX、ONNX、PNG、PDF 等二进制资产。
- `requirements.txt`：Python 分析脚本所需依赖。

## analysis/

- `crowd_bottleneck_visualization.py`：瓶颈识别主脚本。读取 Unity 导出的客流状态 CSV，完成质量检查、时空网格聚合、瓶颈评分、热点事件识别，并输出图表、CSV 表格和 Markdown 报告。
- `control_effect_visualization.py`：管控效果对比脚本。读取 `ControlExport/` 中的实验指标，生成总量 KPI、碰撞次数、分区峰值密度和最大人数对比图。
- `control_measures_visualization.py`：管控措施可视化脚本。汇总不同管控方案的核心指标，生成报告和说明性图表。
- `generate_arrival_chart.py`：到达客流可视化脚本。将客流到达数据按时间聚合，并生成 HTML 图表与预览图。
- `generate_crowd_training_chart.py`：训练曲线整理脚本。解析训练日志和 checkpoint 信息，生成可浏览的训练过程曲线。
- `build_model_explanation_docx.py`：模型说明文档生成脚本。用于生成包含模型结构、指标和公式说明的 Word 文档。
- `rewrite_chapter4_stg_cbi.py`：论文第四章辅助改写脚本，用于替换或插入 STG-CBI 相关文字内容。
- `crowd_analysis_normal_output/`：常态客流场景的瓶颈识别结果，包括活跃人数曲线、密度热力图、速度热力图、瓶颈评分图、热点事件表和分析报告。
- `crowd_analysis_burst_output/`：突发客流场景的瓶颈识别结果，文件类型与常态场景相同。
- `control_analysis_output/`：管控效果分析输出，包括 KPI 仪表盘、碰撞对比、分区密度/人数对比、汇总 CSV 和报告。
- `crowd_stable_60agents_v1_training_curve.html` / `crowd_stable_60agents_v1_training_curve_preview.png`：60 智能体训练过程可视化结果。
- `shenzhenbei_arrival_flow_20260301.html` / `shenzhenbei_arrival_flow_20260301_preview.png`：深圳北站到达客流分布可视化结果。

## unity/

- `Packages/manifest.json`：Unity 包依赖清单，包含 ML-Agents、AI Navigation、ProBuilder、TextMeshPro 等。
- `Packages/packages-lock.json`：Unity 包锁定文件，用于复现依赖版本。
- `ProjectSettings/`：Unity 工程设置，包含项目版本、图形、物理、输入、质量和构建场景配置。
- `ControlExport/`：Unity 管控实验导出的指标 CSV，供 Python 脚本分析管控措施效果。
- `crowd_state_normal.csv`：常态客流仿真的网格状态采样数据。
- `crowd_state_Burst.csv`：突发客流仿真的网格状态采样数据。

## unity/Assets/

- `Scenes/SceneTraining_Shenzhenbei.unity`：深圳北站 FBX 模型上的多智能体训练场景。
- `Scenes/Scene_FBX_CrowdInference.unity`：加载训练后模型进行客流推理的核心场景。
- `Scenes/Scene_FBX_BurstControl.unity`：突发客流与管控策略实验场景。
- `Scenes/Scene_Training_Crowd.unity`：简化通道环境训练场景。
- `Scenes/FinalScene_CrowdRuntime.unity`：运行时客流生成与统计场景。
- `Scenes/shenzhenbei_main.unity`：深圳北站主体空间和导航网格基础场景。
- `Scenes/SampleScene.unity`：Unity 默认样例场景，保留用于工程完整性。
- `Scenes/shenzhenbei_main/*.asset`：导航网格和场景辅助资产。
- `Scripts/BottleneckZoneMonitor.cs`：区域瓶颈监测组件，统计区域人数、密度、速度、临界状态和持续时间。
- `Scripts/CrowdPedestrianAgent.cs`：多智能体训练环境中的行人 Agent，包含邻居观测、动作执行、奖励和碰撞处理。
- `Scripts/CrowdStateLogger.cs`：运行时按网格采样行人状态并写出 CSV。
- `Scripts/CrowdTrainingManager.cs`：简化通道训练场景的行人生成、目标门分配和训练统计。
- `Scripts/FBXCrowdPedestrianAgent.cs`：面向深圳北站 FBX 场景的训练型行人 Agent。
- `Scripts/FBXCrowdTrainingManager.cs`：FBX 场景中的训练智能体管理器。
- `Scripts/FBXFinalSceneController.cs`：FBX 场景运行时客流生成、到达/失败统计和回收控制。
- `Scripts/FBXRuntimePedestrianAgent.cs`：FBX 场景运行时推理行人 Agent。
- `Scripts/FBXTrafficControlManager.cs`：突发客流管控策略管理器，负责限流、导向和实验指标导出。
- `Scripts/FinalSceneController.cs`：非 FBX 场景运行时客流控制器。
- `Scripts/PedestrianAgent.cs`：基础单行人 ML-Agents 训练 Agent。
- `Scripts/RuntimePedestrianAgent_modified.cs`：运行时行人 Agent 修改版，支持空间缩放、邻居观测和边界检查。
- `Scripts/SimulationSpace.cs`：真实世界坐标与训练局部坐标之间的转换组件。
- `prefabs/`：行人训练、推理和运行时预制体。
- `config/*.yaml`：ML-Agents PPO 训练配置，包括基础行人、群体行人、稳定训练和 FBX 场景训练配置。
- `results/crowd_fbx_full_v1/PedestrianCrowdFBX.onnx`：FBX 场景群体行人最终推理模型。
- `results/crowd_stable_60agents_v1/PedestrianCrowd.onnx`：稳定 60 智能体训练得到的最终推理模型。
- `Resources/BillingMode.json` 和 `BillingMode.json`：Unity 服务/计费模式配置文件，随原工程保留。
- `TextMesh Pro/`：TextMeshPro 默认字体、材质、着色器和文档资源。
- `SimulationResults/metrics.csv`：Unity 场景内生成的示例指标结果。

## 已排除内容

- Unity 本地生成目录：`Library/`、`Logs/`、`UserSettings/`、`Temp/`、`Obj/`、`Build/`、`Builds/`。
- 训练中间产物：`.pt` 权重、TensorBoard `events.out.tfevents*`、checkpoint 日志、运行日志。
- 未被核心场景引用的大型 FBX：`深圳北站_仿真用.FBX`、`客流管控.FBX`。
- 中间 checkpoint ONNX，仅保留预制体直接引用的最终 ONNX 模型。

