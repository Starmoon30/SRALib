# SRALib XML API 与功能说明

本文档记录 SRALib 当前可由 XML 复用的主要功能、实现入口和推荐写法。

约定：

- 命名空间统一为 `SRA`。
- `Class="SRA.xxx"` 写在 `comps/li`、`modExtensions/li`、`hediff comps/li` 或自定义节点上。
- 文档里的 XML 片段都是最小示例，实际 Def 仍需要补齐原版必需字段。
- 推荐在 Def 中写 keyed 本地化键，不推荐直接写死显示文本。旧接口中已有的 literal 字段会单独标注。
- `Vector2` 写法通常是 `(x, y)`，`Vector3` 写法通常是 `(x, y, z)`。
- 列表写法遵循 RimWorld XML：`<field><li>...</li></field>`。
- 缺少关键 Def 引用时，大多数新接口会静默停用或跳过该条目，而不是使用占位符。

## 目录

- 建筑组件
- Pawn 与 Hediff 组件
- 屏障系统
- 武器、炮塔与投射物
- 爆炸、伤害与器官命中
- 事件系统
- 飞越与支援系统
- 专用/遗留组件速查
- 本地化键

## 建筑组件

### 远程地图监控

入口：

```xml
<li Class="SRA.CompProperties_RemoteMapMonitor">
  ...
</li>
```

实现：

- 给建筑添加选择世界目标、打开远程地图、断开链接的 gizmo。
- 链接目标是 `MapParent`。
- 若目标是未敌对势力的据点，会禁止选择或打开，避免原版据点地图因没有敌对 pawn 立即结算为摧毁。
- 可选科技需求、供电需求、自定义图标和本地化 key。
- `keepMapAliveWhenLinked` 为 true 时，链接期间通过 patch 阻止目标地图被原版清理。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `selectTargetLabelKey` | `SRA_RemoteMonitoring_SelectTargetLabel` | 选择目标按钮标题 key |
| `selectTargetDescKey` | `SRA_RemoteMonitoring_SelectTargetDesc` | 选择目标按钮说明 key |
| `openTargetLabelKey` | `SRA_RemoteMonitoring_OpenTargetLabel` | 打开地图按钮标题 key |
| `openTargetDescKey` | `SRA_RemoteMonitoring_OpenTargetDesc` | 打开地图按钮说明 key |
| `disconnectTargetLabelKey` | `SRA_RemoteMonitoring_DisconnectTargetLabel` | 断开按钮标题 key |
| `disconnectTargetDescKey` | `SRA_RemoteMonitoring_DisconnectTargetDesc` | 断开按钮说明 key |
| `targetSelectionPromptKey` | `SRA_RemoteMonitoring_TargetSelectionPrompt` | 世界目标选择提示 key |
| `noTargetMessageKey` | `SRA_RemoteMonitoring_NoTargetMessage` | 无目标消息 key |
| `invalidTargetMessageKey` | `SRA_RemoteMonitoring_InvalidTargetMessage` | 目标失效消息 key |
| `invalidSelectionMessageKey` | `SRA_RemoteMonitoring_InvalidSelectionMessage` | 非法选择消息 key |
| `linkEstablishedMessageKey` | `SRA_RemoteMonitoring_LinkEstablishedMessage` | 链接成功消息 key，参数 `{0}` 为目标标签 |
| `linkDisconnectedMessageKey` | `SRA_RemoteMonitoring_LinkDisconnectedMessage` | 断开消息 key |
| `openFailedMessageKey` | `SRA_RemoteMonitoring_OpenFailedMessage` | 打开失败消息 key |
| `inspectStringKey` | `SRA_RemoteMonitoring_InspectString` | 检查栏文本 key，参数 `{0}` 为目标标签 |
| `researchRequiredMessageKey` | `SRA_RemoteMonitoring_ResearchRequiredMessage` | 科技未完成消息 key，参数 `{0}` 为科技名 |
| `nonHostileSettlementMessageKey` | `SRA_RemoteMonitoring_NonHostileSettlementMessage` | 未敌对据点拒绝消息 key |
| `selectIconPath` | `SRA/UI/Commands/UI_SRA_RemoteMonitoring` | 选择目标图标路径 |
| `openIconPath` | `SRA/UI/Commands/UI_SRA_RemoteMonitoring` | 打开地图图标路径 |
| `disconnectIconPath` | `SRA/UI/Commands/UI_SRA_RemoteMonitoringClose` | 断开图标路径 |
| `requiredResearch` | null | 需要完成的科技 |
| `requirePower` | true | 是否需要供电 |
| `allowWorldTargetSelection` | true | 是否显示选择目标按钮 |
| `allowDisconnect` | true | 是否显示断开按钮 |
| `allowRemoteArtilleryCommands` | true | 是否显示跨地图火炮调度按钮 |
| `jumpToMapAfterOpen` | true | 打开后是否切换视角到地图 |
| `keepMapAliveWhenLinked` | true | 链接期间是否保持远程地图不被清理 |

示例：

```xml
<li Class="SRA.CompProperties_RemoteMapMonitor">
  <requiredResearch>LongRangeMineralScanner</requiredResearch>
  <requirePower>true</requirePower>
  <selectIconPath>SRA/UI/Commands/UI_SRA_RemoteMonitoring</selectIconPath>
  <openIconPath>SRA/UI/Commands/UI_SRA_RemoteMonitoring</openIconPath>
  <disconnectIconPath>SRA/UI/Commands/UI_SRA_RemoteMonitoringClose</disconnectIconPath>
</li>
```

### 解除限时天气/环境效果

入口：

```xml
<li Class="SRA.CompProperties_ClearTimedGameConditions">
  ...
</li>
```

实现：

- 给建筑添加一个按钮。
- 点击后移除当前地图上所有“可判断剩余时间”的非永久 `GameCondition` 和限时天气。
- 白名单是不解除的天气或环境效果。
- 若 `requirePower=true` 且建筑有 `CompPowerTrader`，断电时按钮不可用。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `buttonLabelKey` | `SRA_ClearTimedGameConditions_Label` | 按钮标题 key |
| `buttonDescKey` | `SRA_ClearTimedGameConditions_Desc` | 按钮说明 key |
| `noTargetMessageKey` | `SRA_ClearTimedGameConditions_NoTargetMessage` | 无可解除目标消息 key |
| `clearedMessageKey` | `SRA_ClearTimedGameConditions_ClearedMessage` | 解除完成消息 key，参数 `{0}` 数量，`{1}` 名称列表 |
| `powerRequiredMessageKey` | `SRA_ClearTimedGameConditions_PowerRequiredMessage` | 断电禁用消息 key |
| `iconPath` | `SRA/UI/Commands/UI_SRA_ClearTimedGameConditions` | 按钮图标 |
| `requirePower` | true | 是否需要供电 |
| `gameConditionWhitelist` | null | 不解除的 `GameConditionDef` |
| `weatherWhitelist` | null | 不解除的 `WeatherDef` |

示例：

```xml
<li Class="SRA.CompProperties_ClearTimedGameConditions">
  <requirePower>true</requirePower>
  <gameConditionWhitelist>
    <li>SolarFlare</li>
  </gameConditionWhitelist>
  <weatherWhitelist>
    <li>Clear</li>
    <li>FoggyRain</li>
  </weatherWhitelist>
</li>
```

### Hediff 探测器

入口：

```xml
<li Class="SRA.CompProperties_HediffDetector">
  ...
</li>
```

实现：

- 按设定间隔扫描范围内 Pawn；`detectRadius<=0` 时扫描整张地图，大于 0 时仅扫描圆形范围。
- `detectionList` 可配置任意 HediffDef，并在带有该 Hediff 的 Pawn 头顶绘制图标。不存在的 HediffDef 会静默跳过，因此可直接填写可选 DLC 或其他模组的 Hediff。条目还可通过 `matchAllInvisibilityHediffs=true` 匹配所有使用原版 `HediffComp_Invisibility` 的 Hediff，无需穷举具体 Def。
- 头顶标记的贴图和材质缓存仅在主线程解析，以满足 Unity 的渲染资源约束。非主线调用会跳过当次解析，并由后续主线程扫描安全建立缓存。
- 可选揭露迷雾。`detectRadius>0` 时揭露圆形范围内的连通迷雾；`detectRadius<=0` 时清除整张地图的所有迷雾。两种模式都使用分帧任务，每 tick 的处理格数受全图共享预算限制。
- 可选压制敌对 Pawn 的原版隐形，包括皇权的灵能隐身与异常的隐形效果。不依赖特定 DLC 开关；游戏未加载原版隐形组件时，仅反隐功能自动不执行。
- 反隐通过原版 `HediffComp_Invisibility.ForcedVisible` 的兼容补丁和短时缓存生效，不会移除 Hediff；缓存到期后恢复原版隐形行为。
- 同地图的扫描请求由 `MapComponent_HediffDetectorManager` 排队处理，每 tick 最多执行一轮 Pawn 扫描和一轮迷雾扫描，避免多个建筑集中遍历 Pawn 或扩散迷雾。
- `requirePower=true` 且建筑有 `CompPowerTrader` 时，断电期间会停止扫描并隐藏已有标记；没有电力 Comp 的建筑不受影响。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `detectRadius` | 0 | 探测半径；小于等于 0 扫描整张地图，并在 `revealFog=true` 时清除全图迷雾；正数仅扫描圆形范围 |
| `scanIntervalTicks` | 250 | 扫描间隔，单位为 tick；小于 1 时按 1 处理 |
| `revealFog` | false | 是否揭露迷雾；全图模式下清除整张地图的迷雾 |
| `detectionList` | 空 | Hediff 探测图标列表；每项包含 `iconPath`、可选 `hediffDef`和可选 `matchAllInvisibilityHediffs` |
| `disruptEnemyInvisibility` | true | 是否压制范围内敌对 Pawn 的原版隐形；无需为任何 DLC 开关单独配置 |
| `disruptionDurationTicks` | 0 | 强制可见持续时间；小于等于 0 时自动使用扫描间隔加缓冲，确保两次扫描之间不会失效 |
| `requirePower` | true | 是否要求通电；仅当建筑具有 `CompPowerTrader` 时生效 |
| `startEnabled` | false | 建筑生成时是否默认开启；可由 gizmo 手动切换 |
| `gizmoLabelKey` | `SRA_HediffDetector_ToggleLabel` | 开关 gizmo 标题的 Keyed 本地化键 |
| `gizmoDescKey` | `SRA_HediffDetector_ToggleDesc` | 开关 gizmo 说明的 Keyed 本地化键 |
| `uiIconPathEnabled` | `UI/Commands/Attack` | 开启状态的 gizmo 图标路径 |
| `uiIconPathDisabled` | `UI/Commands/Attack` | 关闭状态的 gizmo 图标路径 |
| `markScale` | 2.5 | 头顶标记缩放 |
| `markHeightOffset` | 1.5 | 头顶标记 Z 轴偏移 |
| `markBobbingFrequency` | 0.3 | 标记浮动频率；小于等于 0 时不浮动 |
| `markBobbingAmplitude` | 0.3 | 标记浮动幅度；小于等于 0 时不浮动 |

示例：

```xml
<li Class="SRA.CompProperties_HediffDetector">
  <!-- 半径 35 格；填 0 则扫描并清除整张地图的迷雾。 -->
  <detectRadius>35</detectRadius>
  <scanIntervalTicks>120</scanIntervalTicks>
  <revealFog>true</revealFog>

  <!-- 隐形通配标记：不需穷举 PsychicInvisibility 等具体 HediffDef。 -->
  <detectionList>
    <li>
      <matchAllInvisibilityHediffs>true</matchAllInvisibilityHediffs>
      <iconPath>UI/Commands/Attack</iconPath>
    </li>
  </detectionList>

  <disruptEnemyInvisibility>true</disruptEnemyInvisibility>
  <disruptionDurationTicks>180</disruptionDurationTicks>
  <requirePower>true</requirePower>
  <startEnabled>false</startEnabled>
  <gizmoLabelKey>SRA_HediffDetector_ToggleLabel</gizmoLabelKey>
  <gizmoDescKey>SRA_HediffDetector_ToggleDesc</gizmoDescKey>
  <uiIconPathEnabled>UI/Commands/Attack</uiIconPathEnabled>
  <uiIconPathDisabled>UI/Commands/Attack</uiIconPathDisabled>
  <markScale>2.5</markScale>
  <markHeightOffset>1.5</markHeightOffset>
  <markBobbingFrequency>0.3</markBobbingFrequency>
  <markBobbingAmplitude>0.3</markBobbingAmplitude>
</li>
```

### 低温研究舱

入口：

```xml
<li Class="SRA.CompProperties_CasketResearch">
  ...
</li>
```

实现：

- 此 Comp 必须挂载在 `Building_CryptosleepCasket` 或其子类上，以使用原版低温舱的收纳、搬运与弹出逻辑。Pawn 处于低温舱内时，其状态、需求和任务会被冻结。
- 建筑提供“装入研究单元”按钮，点击后在地图上选取任意存活 Pawn，不再生成全地图列表。属于玩家派系且不是囚犯的清醒 Pawn（包括驯养动物）会自行进入；殖民地囚犯与倒地目标则由可用的己方植民者搬入。囚犯的搬运遵循原版裂解扫描仪的规则，无需先将其击倒；其他清醒 Pawn 不会被强制搬运，必须先倒地。
- 已被收容的任意存活 Pawn 都可提供研究，包括动物、敌人和客人。
- 研究能力正常时，研究工作量每秒等于 Pawn 的 `ResearchSpeed` × 60 × 建筑的 `ResearchSpeedFactor` × `researchSpeedFactor`。没有研究能力时，改用 `incapableResearchSpeed` （默认 `6`，即基础研究速度的 10%）。内部仅在调用原版 `ResearchManager.ResearchPerformed` 前换算为每 tick 工作量，因此仍会正常受难度、科技等级成本系数、完成信件和研究统计影响。
- 仅研究玩家当前选定的常规科技，并使用原版 `CanStartNow` 判定。未选择科技、科技已完成、前置不足、科技图纸/分析/机械师/研究设施条件未满足，或选中异常知识项目时不会产生进度。
- 已断电或不具备研究能力的收容对象不会提供进度；详细原因会显示在检查面板中。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `researchIntervalTicks` | 250 | 研究结算间隔，单位为 tick；必须大于等于 1 |
| `researchSpeedFactor` | 1.0 | 额外研究乘数；与 Pawn 的 `ResearchSpeed` 和建筑的 `ResearchSpeedFactor` 相乘 |
| `incapableResearchSpeed` | 6 | 没有研究能力的 Pawn 每秒使用的研究工作量；`6` 等同于基础研究速度的 10% |
| `requirePower` | true | 是否要求建筑具有已通电的 `CompPowerTrader`；关闭后可用于无需供电的建筑 |
| `localization` | 见下文 | 研究舱全部 Keyed 本地化键；可替换为所属模组提供的键名，字段不接受直接显示文本 |

`localization` 整段可省略，此时使用 SRALib 默认键。若配置该段，应在所属模组的各语言 `Keyed.xml` 中定义下列所有被替换的键，并保留与默认键相同的参数数量。

示例：

```xml
<ThingDef ParentName="BuildingBase">
  <defName>Example_ResearchCasket</defName>
  <label>研究低温舱</label>
  <!-- 原版低温舱负责收容 Pawn，并在收容期间冻结其状态。 -->
  <thingClass>Building_CryptosleepCasket</thingClass>
  <containedPawnsSelectable>true</containedPawnsSelectable>
  <tickerType>Normal</tickerType>
  <comps>
    <li Class="SRA.CompProperties_CasketResearch">
      <researchIntervalTicks>250</researchIntervalTicks>
      <researchSpeedFactor>1.0</researchSpeedFactor>
      <!-- 研究速度的单位为每秒；6 等同于基础研究速度的 10%。 -->
      <incapableResearchSpeed>6</incapableResearchSpeed>
      <requirePower>true</requirePower>
      <!-- 所有文本均填写 Keyed 本地化键，可按需替换为本模组的键。 -->
      <localization>
        <loadSubjectLabelKey>Example_ResearchCasket_LoadSubject</loadSubjectLabelKey>
        <loadSubjectDescKey>Example_ResearchCasket_LoadSubjectDesc</loadSubjectDescKey>
        <loadOccupiedKey>Example_ResearchCasket_LoadOccupied</loadOccupiedKey>
        <invalidSubjectKey>Example_ResearchCasket_InvalidSubject</invalidSubjectKey>
        <loadSubjectMustBeDownedKey>Example_ResearchCasket_LoadSubjectMustBeDowned</loadSubjectMustBeDownedKey>
        <loadNoCarrierKey>Example_ResearchCasket_LoadNoCarrier</loadNoCarrierKey>
        <loadUnreachableKey>Example_ResearchCasket_LoadUnreachable</loadUnreachableKey>
        <invalidHostKey>Example_ResearchCasket_InvalidHost</invalidHostKey>
        <noSubjectKey>Example_ResearchCasket_NoSubject</noSubjectKey>
        <noPowerKey>Example_ResearchCasket_NoPower</noPowerKey>
        <noProjectKey>Example_ResearchCasket_NoProject</noProjectKey>
        <workingKey>Example_ResearchCasket_Working</workingKey>
        <speedKey>Example_ResearchCasket_Speed</speedKey>
        <incapableSpeedKey>Example_ResearchCasket_IncapableSpeed</incapableSpeedKey>
        <configRequiresCryptosleepCasketKey>Example_ResearchCasket_ConfigRequiresCryptosleepCasket</configRequiresCryptosleepCasketKey>
        <configInvalidIntervalKey>Example_ResearchCasket_ConfigInvalidInterval</configInvalidIntervalKey>
        <configInvalidSpeedFactorKey>Example_ResearchCasket_ConfigInvalidSpeedFactor</configInvalidSpeedFactorKey>
        <configInvalidIncapableSpeedKey>Example_ResearchCasket_ConfigInvalidIncapableSpeed</configInvalidIncapableSpeedKey>
      </localization>
    </li>
    <li Class="CompProperties_Power">
      <compClass>CompPowerTrader</compClass>
      <basePowerConsumption>400</basePowerConsumption>
    </li>
  </comps>
  <inspectorTabs>
    <li>ITab_ContentsCasket</li>
  </inspectorTabs>
</ThingDef>
```

### 气密门

入口：

```xml
<thingClass>SRA.Building_VacDoor</thingClass>
```

实现：

- 基于原版 `Building_SupportedDoor`。
- 通电关闭时不交换真空，也不进行门两侧温度交换。
- 断电或非气密状态下退回原版门的真空/温度交换逻辑。
- 没有 `CompPowerTrader` 时视为不需要供电，按通电处理。
- 可通过 `VacDoorExtension` 阻止未释放囚犯正常开门，包括越狱时由原版 `CanOpenAnyDoor` 取得的强开权限。
- 防撬开关只阻止“正常开门”，不会阻止囚犯攻击、破坏或爆破门。

`modExtensions/li` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `preventPrisonerPrying` | false | 是否阻止未释放囚犯正常打开该门 |

示例：

```xml
<ThingDef ParentName="DoorBase">
  <defName>SRA_SecureVacDoor</defName>
  <thingClass>SRA.Building_VacDoor</thingClass>
  <modExtensions>
    <li Class="SRA.VacDoorExtension">
      <preventPrisonerPrying>true</preventPrisonerPrying>
    </li>
  </modExtensions>
</ThingDef>
```

### 建筑损伤抵抗

入口：

```xml
<li Class="SRA.CompProperties_BuildingDamageAdjuster">
  ...
</li>
```

实现：

- 在建筑 `PostPreApplyDamage` 阶段调整 `DamageInfo.Amount`。
- 顺序为：先套上限，再做固定降低，再乘损伤效率。
- 最终伤害小于等于 0 时设置 `absorbed=true`。
- 配置项会显示在建筑细则中，只有非默认项才显示。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `damageTakenMult` | 1 | 损伤效率，最终伤害乘数 |
| `damageTakenMax` | 0 | 单次伤害上限，大于 0 生效 |
| `damageTakenReduce` | 0 | 固定伤害降低，大于 0 生效 |
| `onlyAffectHarmfulDamage` | true | 仅影响 `harmsHealth=true` 的伤害 |

示例：

```xml
<li Class="SRA.CompProperties_BuildingDamageAdjuster">
  <damageTakenMult>0.5</damageTakenMult>
  <damageTakenMax>30</damageTakenMax>
  <damageTakenReduce>5</damageTakenReduce>
</li>
```

### 战争单元生成器

入口：

```xml
<li Class="SRA.CompProperties_SRAWarUnitSpawner">
  ...
</li>
```

实现：

- 从子 mod 迁移的战争单元生产/部署建筑组件。
- 不再有默认占位单位。`units` 没有有效条目时组件不启用，不显示按钮，不 tick。
- 多个单位类型可配置，但生产只推进当前选择的单位。
- 切换单位类型会清空所有库存和生产进度。
- 自动迎击只部署当前选择单位。
- 建筑检查说明中只显示当前选择并正在组装的单位状态，不列出其它候选单位。
- `deathCountdownHediffDef` 可选，留空时不添加任何限时 hediff。
- 生产和敌袭扫描分离为不同间隔，并按 `thingIDNumber` 错峰。

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `units` | 空 | 可生产单位列表。空则停用 |
| `deathCountdownHediffDef` | null | 生成单位后附加的 hediff |
| `deathCountdownHediffSeverity` | 1 | 附加 hediff 严重度 |
| `autoModeDefault` | true | 默认是否自动迎击 |
| `requirePower` | true | 是否需要供电 |
| `productionCheckIntervalTicks` | 300 | 生产推进间隔 |
| `threatCheckIntervalTicks` | 300 | 自动迎击检测间隔 |
| `spawnRadius` | 3.9 | 生成点搜索半径 |
| `allowManualSpawn` | true | 是否允许手动部署 |
| `allowUnitSelection` | true | 多单位时是否允许切换 |
| `forceGenerateNewPawn` | true | Pawn 生成请求参数 |
| `allowDead` | false | Pawn 生成请求参数 |
| `allowDowned` | false | Pawn 生成请求参数 |
| `canGeneratePawnRelations` | false | Pawn 生成请求参数 |
| `mustBeCapableOfViolence` | true | Pawn 生成请求参数 |
| `generationContext` | `NonPlayer` | Pawn 生成上下文 |

`units/li` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `pawnKindDef` | null | 生成的 `PawnKindDef`。缺失则忽略该条目 |
| `labelKey` | null | 该单位在 UI 中显示的 keyed 文本；缺省用 `PawnKindDef.LabelCap` |
| `generationTicks` | 3000 | 生产 1 个单位所需 tick |
| `maxStored` | 1 | 当前模式最大库存 |

本地化配置：

```xml
<localization>
  <keyPrefix>SRA_MyWarUnitSpawner</keyPrefix>
</localization>
```

若只写 `keyPrefix`，会自动使用这些 key：

```xml
SRA_MyWarUnitSpawner_AutoMode
SRA_MyWarUnitSpawner_AutoModeDesc
SRA_MyWarUnitSpawner_Deploy
SRA_MyWarUnitSpawner_DeployDesc
SRA_MyWarUnitSpawner_ChangeUnit
SRA_MyWarUnitSpawner_ChangeUnitDesc
SRA_MyWarUnitSpawner_StatusHeader
SRA_MyWarUnitSpawner_StatusLine
SRA_MyWarUnitSpawner_SelectionOption
SRA_MyWarUnitSpawner_DisabledNoUnit
SRA_MyWarUnitSpawner_DisabledNoPower
SRA_MyWarUnitSpawner_DisabledNoStock
SRA_MyWarUnitSpawner_NoUnit
```

也可以单项覆盖：

```xml
<localization>
  <deployLabelKey>SRA_CustomDeployLabel</deployLabelKey>
  <statusHeaderKey>SRA_CustomSpawnerStatusHeader</statusHeaderKey>
</localization>
```

示例：

```xml
<li Class="SRA.CompProperties_SRAWarUnitSpawner">
  <units>
    <li>
      <pawnKindDef>SRA_Mech_WarUnit_M_D</pawnKindDef>
      <labelKey>SRA_WarUnit_Defender</labelKey>
      <generationTicks>30000</generationTicks>
      <maxStored>2</maxStored>
    </li>
    <li>
      <pawnKindDef>SRA_Mech_WarUnit_M_A</pawnKindDef>
      <labelKey>SRA_WarUnit_Assault</labelKey>
      <generationTicks>18000</generationTicks>
      <maxStored>4</maxStored>
    </li>
  </units>
  <deathCountdownHediffDef>SRA_30000_CountdownDeath</deathCountdownHediffDef>
  <requirePower>true</requirePower>
  <productionCheckIntervalTicks>300</productionCheckIntervalTicks>
  <threatCheckIntervalTicks>300</threatCheckIntervalTicks>
  <localization>
    <keyPrefix>SRA_MyWarUnitSpawner</keyPrefix>
  </localization>
</li>
```

### 复活信标

入口：

```xml
<li Class="SRA.CompProperties_ResurrectionBeacon">
  ...
</li>
```

实现：

- 建筑提供绑定 UI，可绑定当前地图单位。
- Patch `Pawn.Kill`，当被绑定 pawn 即将死亡时，寻找可用信标并拦截死亡。
- 拦截后把 pawn 传送到信标附近，并可选添加 hediff。
- 使用 `MapComponent_ResurrectionBeaconManager` 按 pawn 缓存信标，避免死亡时全图扫描。
- 信标被摧毁时会解绑并可清理绑定 hediff。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `boundHediffDef` | null | 绑定期间添加的状态 hediff |
| `resurrectionHediffDef` | null | 拦截死亡后添加的 hediff |
| `replaceExistingResurrectionHediff` | true | 再次拦截时是否刷新 hediff |
| `resurrectionHediffSeverity` | 1 | 复活 hediff 严重度 |
| `requireCanTakeOrderToBind` | true | 仅允许可被玩家命令的 pawn 绑定 |
| `requirePlayerFactionForGizmo` | true | 仅玩家阵营信标显示 UI |
| `requirePower` | false | 是否需要供电 |
| `removeDeadBindings` | true | 清理已死亡绑定 |
| `removeBoundHediffOnUnbind` | true | 解绑时清理绑定 hediff |
| `placeNearBeacon` | true | 是否传送到信标附近可行走格 |
| `teleportCellRadius` | 4 | 附近格搜索半径 |
| `useTeleportFlecks` | true | 是否播放传送 fleck |
| `resurrectionPriority` | 0 | 多个信标竞争时的优先级 |
| `commandIconPath` | `UI/Commands/DropCarriedPawn` | 绑定按钮图标 |
| `bindCommandLabelKey` 等 | `SRA_ResurrectionBeacon_*` | UI 本地化 key |

示例：

```xml
<li Class="SRA.CompProperties_ResurrectionBeacon">
  <boundHediffDef>SRA_BoundToBeacon</boundHediffDef>
  <resurrectionHediffDef>SRA_ResurrectionSleep</resurrectionHediffDef>
  <requirePower>true</requirePower>
  <teleportCellRadius>5</teleportCellRadius>
  <resurrectionPriority>10</resurrectionPriority>
</li>
```

### 自动修复塔

入口：

```xml
<li Class="SRA.CompProperties_RepairTower">
  ...
</li>
```

实现：

- 每 300 tick 检查一次。
- 有电力组件时需要供电。
- 修复建筑、物品，以及玩家 pawn 身上的装备/服装/物品。
- 建筑可被修到超过原最大耐久：`MaxHitPoints * maxRepairMultiplier + maxRepairOffset`。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `repairRadius` | 0 | 修复半径，0 表示全图 |
| `repairArea` | `HomeArea` | `HomeArea` 或 `EntireArea` |
| `repairRatePerSecond` | 0.02 | 每秒按 `MaxHitPoints` 百分比修复 |
| `maxRepairMultiplier` | 2 | 建筑修复上限倍率 |
| `maxRepairOffset` | 800 | 建筑修复上限额外值 |

示例：

```xml
<li Class="SRA.CompProperties_RepairTower">
  <repairRadius>30</repairRadius>
  <repairArea>HomeArea</repairArea>
  <repairRatePerSecond>0.02</repairRatePerSecond>
  <maxRepairMultiplier>2</maxRepairMultiplier>
  <maxRepairOffset>800</maxRepairOffset>
</li>
```

### 生成时播放声音

入口：

```xml
<li Class="SRA.CompProperties_PlaySoundOnSpawn">
  ...
</li>
```

实现：

- 物体生成时播放一次声音。
- 支持延迟、派系过滤、镜头位置或物体位置播放。
- 存档重载不会重复播放。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `sound` | null | `SoundDef` |
| `delaySeconds` | 0 | 延迟秒数 |
| `onlyIfPlayerFaction` | false | 仅玩家阵营 |
| `onlyIfHostileFaction` | false | 仅敌对阵营 |
| `onlyIfNeutralFaction` | false | 仅中立阵营 |
| `volume` | 1 | 音量倍率 |
| `pitch` | 1 | 音高倍率 |
| `playOnCamera` | false | 在镜头播放 |
| `playAtThingPosition` | true | 在物体位置播放 |

示例：

```xml
<li Class="SRA.CompProperties_PlaySoundOnSpawn">
  <sound>ShipTakeoff</sound>
  <delaySeconds>0.5</delaySeconds>
  <onlyIfPlayerFaction>true</onlyIfPlayerFaction>
  <volume>1.2</volume>
</li>
```

## Pawn 与 Hediff 组件

### 活力源流：需求最低值

入口：

```xml
<li Class="SRA.HediffCompProperties_SRANeedMin" />
```

实现：

- 每 tick 检查携带者所有需求。
- 任意需求低于 0.05 时拉回 0.05。
- 不限定需求类型。

示例：

```xml
<HediffDef>
  <defName>SRA_VitalityFlux</defName>
  <comps>
    <li Class="SRA.HediffCompProperties_SRANeedMin" />
  </comps>
</HediffDef>
```

### 再生

入口：

```xml
<li Class="SRA.HediffCompProperties_SRARegen">
  ...
</li>
```

实现：

- 按 `checkInterval` 周期治疗伤口。
- 可逐步恢复缺失部位。
- 添加部件下方的部位会被跳过，避免覆盖仿生/义体结构。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `checkInterval` | 60 | 检查间隔 tick |
| `healPerSecond` | 1 | 每秒治疗量 |

示例：

```xml
<li Class="SRA.HediffCompProperties_SRARegen">
  <checkInterval>60</checkInterval>
  <healPerSecond>2</healPerSecond>
</li>
```

### 忽略地形移动成本

入口：

```xml
<li Class="SRA.HediffCompProperties_IgnoreTerrainCost" />
```

实现：

- hediff 添加时把 pawn 加入缓存，移除时减少计数。
- Patch `Pawn_PathFollower.CostToMoveIntoCell` 和 `GetPawnCellBaseCostOverride`。
- 移动只按 pawn 自身 cardinal/diagonal ticks 计算，不吃地形成本。

示例：

```xml
<li Class="SRA.HediffCompProperties_IgnoreTerrainCost" />
```

### 倒计时死亡

入口：

```xml
<hediffClass>SRA.Hediff_CountdownDeath</hediffClass>
<comps>
  <li Class="SRA.HediffCompProperties_CountdownTimer">
    <countdownDuration>60000</countdownDuration>
  </li>
</comps>
```

实现：

- hediff 创建后开始倒计时。
- 到时调用 `Pawn.Kill`。
- 玩家阵营 pawn 会显示立即自毁 gizmo。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `countdownDuration` | 60000 | 倒计时 tick |

### 武器/模式切换

入口：

```xml
<li Class="SRA.HediffCompProperties_WeaponSwitcher">
  ...
</li>
```

实现：

- 给玩家 pawn 添加武器切换 gizmo。
- `linkedWeapons` 是可互相切换的一组武器。
- 切换时创建新武器，尽量继承当前武器品质，并销毁旧武器。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `linkedWeapons` | null | 可切换武器列表 |
| `gizmoIconPath` | `SRA/UI/Commands/UI_SRA_WeaponSwitcher` | 图标路径 |

示例：

```xml
<li Class="SRA.HediffCompProperties_WeaponSwitcher">
  <linkedWeapons>
    <li>SRA_Rifle_ModeA</li>
    <li>SRA_Rifle_ModeB</li>
  </linkedWeapons>
</li>
```

## 屏障系统

入口：

```xml
<li Class="SRA.HediffCompProperties_SRABarrier">
  ...
</li>
```

实现：

- HediffComp 形式的屏障，使用 Harmony 前缀拦截 `Pawn.PreApplyDamage`。
- 多个屏障按 `priority` 从高到低尝试吸收。
- 屏障值会显示 gizmo，即使非玩家可控单位被选中也会显示。
- 非伤害性 `DamageDef` 默认可被屏障完全吸收，除非 `Damage_SRABarrier_factor_Extension` 定义其它行为。
- 当屏障把 `dinfo.Amount` 降为 0，会设置 `absorbed=true`。
- 可抵抗眩晕、精神状态、原版 `CatatonicBreakdown`、原版 `PorcupineQuill`。
- 硬化屏障使用单位最终护甲参与屏障承伤。
- 偏斜屏障使用单位处理后的 `MeleeDodgeChance` 作为通用闪避率。
- 可通过 `whenDestroy`、`whenRegenFull`、`whenAbsorbDamage` 在破盾、回满、实际吸收伤害后触发自定义效果。
- 屏障首次添加并初始化为满值时也会视为一次 `whenRegenFull`。
- `whenAbsorbDamage` 只在屏障值被实际消耗后触发；偏斜、非伤害直挡、硬化护甲完全挡下、固定减伤归零等直接阻挡不会触发。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `maxBarrier` | 100 | 最大屏障值 |
| `DamageTakenMult` | 1 | 屏障损伤效率 |
| `DamageTakenMax` | 0 | 单次屏障损伤上限，大于 0 生效 |
| `DamageTakenReduce` | 0 | 固定屏障损伤降低，大于 0 生效 |
| `regenRate` | 5 | 每秒回复屏障值 |
| `regenDelay` | 3 | 受击后延迟回复秒数 |
| `rechargeCooldown` | 10 | 破盾后冷却秒数 |
| `RemoveWhenDestroy` | false | 破盾时是否移除 hediff |
| `BlockStunAndMentalState` | false | 心灵壁垒，抵抗眩晕和精神崩溃 |
| `HardenedBarrier` | false | 硬化屏障，使用最终护甲 |
| `DeflectiveBarrier` | false | 偏斜屏障，使用最终近战闪避率 |
| `priority` | 0 | 多屏障吸收优先级 |
| `whenDestroy` | null | 破盾时触发的效果列表 |
| `whenRegenFull` | null | 屏障从未满回复到满值时触发的效果列表 |
| `whenAbsorbDamage` | null | 屏障实际消耗屏障值吸收伤害后触发的效果列表 |

示例：

```xml
<HediffDef>
  <defName>SRA_EnergyBarrier</defName>
  <comps>
    <li Class="SRA.HediffCompProperties_SRABarrier">
      <maxBarrier>250</maxBarrier>
      <DamageTakenMult>0.8</DamageTakenMult>
      <DamageTakenMax>50</DamageTakenMax>
      <DamageTakenReduce>5</DamageTakenReduce>
      <regenRate>10</regenRate>
      <regenDelay>4</regenDelay>
      <rechargeCooldown>12</rechargeCooldown>
      <BlockStunAndMentalState>true</BlockStunAndMentalState>
      <HardenedBarrier>true</HardenedBarrier>
      <DeflectiveBarrier>true</DeflectiveBarrier>
      <priority>10</priority>
      <whenAbsorbDamage>
        <li Class="SRA.SRABarrierEffect_AddHediff">
          <hediffDef>SRA_BarrierReactiveBuff</hediffDef>
          <severity>1</severity>
          <durationTicks>120</durationTicks>
          <cooldownSeconds>10</cooldownSeconds>
        </li>
      </whenAbsorbDamage>
      <whenDestroy>
        <li Class="SRA.SRABarrierEffect_AddHediff">
          <hediffDef>SRA_BarrierBrokenShock</hediffDef>
          <severity>1</severity>
          <cooldownTicks>600</cooldownTicks>
        </li>
      </whenDestroy>
    </li>
  </comps>
</HediffDef>
```

### 屏障触发效果

入口：

```xml
<whenAbsorbDamage>
  <li Class="SRA.SRABarrierEffect_AddHediff">
    ...
  </li>
</whenAbsorbDamage>
```

也可以写在：

```xml
<whenDestroy>...</whenDestroy>
<whenRegenFull>...</whenRegenFull>
```

实现：

- 效果挂在 `HediffCompProperties_SRABarrier` 内部。
- 每个列表都是 `List<SRABarrierEffect>`，通过 `<li Class="SRA.具体效果类">` 选择效果实现。
- 冷却记录保存在每个屏障实例上，不会被同一个 HediffDef 的其它 pawn 共用。
- `cooldownTicks` 优先于 `cooldownSeconds`；两者都不填或不大于 0 时没有冷却。
- `cooldownKey` 可选；填写相同 key 的效果在同一个屏障实例上共用冷却，不填则按列表位置独立冷却。

通用字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `cooldownTicks` | 0 | 触发冷却 tick 数，大于 0 生效 |
| `cooldownSeconds` | 0 | 触发冷却秒数，仅当 `cooldownTicks <= 0` 时生效 |
| `cooldownKey` | null | 可选共享冷却 key |

#### `SRABarrierEffect_AddHediff`

作用：

- 触发时为屏障所在 pawn 添加指定 Hediff。
- 可选择写入全身或指定 `BodyPartDef`。
- 可选择覆盖/叠加已有同 def Hediff 的严重度。
- 如果 Hediff 带 `HediffComp_Disappears`，可以写入持续时间。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `hediffDef` | null | 要添加的 HediffDef，必填 |
| `bodyPartDef` | null | 目标 BodyPartDef，留空则添加到全身 |
| `severity` | -1 | 添加或修改的严重度，小于 0 时保留 hediff 默认值 |
| `affectExisting` | true | 已有同 def Hediff 时是否复用 |
| `addSeverityToExisting` | true | 复用已有 Hediff 时是否叠加严重度；false 为设置严重度 |
| `durationTicks` | -1 | 对带 `HediffComp_Disappears` 的 Hediff 设置剩余 tick，小于 0 时不改 |

示例：

```xml
<whenRegenFull>
  <li Class="SRA.SRABarrierEffect_AddHediff">
    <hediffDef>SRA_BarrierRecovered</hediffDef>
    <severity>1</severity>
    <durationTicks>2500</durationTicks>
    <cooldownSeconds>30</cooldownSeconds>
  </li>
</whenRegenFull>
```

### DamageDef 对屏障倍率

入口：

```xml
<li Class="SRA.Damage_SRABarrier_factor_Extension">
  <damage_SRABarrier_factor>0.5</damage_SRABarrier_factor>
</li>
```

实现：

- 挂在 `DamageDef.modExtensions` 上。
- `damage_SRABarrier_factor >= 0` 时，屏障按 `dinfo.Amount * factor` 计算损伤。
- 留空或小于 0 时使用正常屏障逻辑。

示例：

```xml
<DamageDef>
  <defName>SRA_EMPWeakBarrierDamage</defName>
  <workerClass>DamageWorker_AddInjury</workerClass>
  <modExtensions>
    <li Class="SRA.Damage_SRABarrier_factor_Extension">
      <damage_SRABarrier_factor>2</damage_SRABarrier_factor>
    </li>
  </modExtensions>
</DamageDef>
```

## 武器、炮塔与投射物

### 转速炮塔

入口：

```xml
<thingClass>SRA.Building_TurretGunHasSpeed</thingClass>
```

实现：

- 基于 `Building_Turret` 的重写炮塔。
- 炮塔顶需要旋转到目标方向后才开火。
- 支持自定义转速、禁用自动攻击、自定义被 AI 选为攻击目标的嘲讽度。
- 当前主攻击 `Verb` 的 `<requireLineOfSight>false</requireLineOfSight>` 会像飞越弹一样允许自动索敌越过 LOS 阻挡；实际能否命中仍由该 `Verb` 自己的发射/命中逻辑决定。
- 支持 `Verb_ShootWithOffset` 的多炮管位置、制退和炮口火焰动画。
- 支持武器上的 `CompSustainedShoot` 转火逻辑。

`ModExt_HasSpeedTurret` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `speed` | 1 | 每 tick 最大转动角度 |
| `noautoattack` | false | 是否禁用自动索敌 |

`TauntAttackTargetExtension` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `targetPriorityFactor` | 1 | AI 目标评分倍率 |
| `disabled` | false | 是否对 AI 威胁搜索禁用 |

示例：

```xml
<ThingDef ParentName="BuildingBase">
  <defName>SRA_SpeedTurret</defName>
  <thingClass>SRA.Building_TurretGunHasSpeed</thingClass>
  <building>
    <turretGunDef>SRA_SpeedTurretGun</turretGunDef>
    <turretTopOffset>(0, 0)</turretTopOffset>
  </building>
  <modExtensions>
    <li Class="SRA.ModExt_HasSpeedTurret">
      <speed>2.5</speed>
      <noautoattack>false</noautoattack>
    </li>
    <li Class="SRA.TauntAttackTargetExtension">
      <targetPriorityFactor>3</targetPriorityFactor>
    </li>
  </modExtensions>
</ThingDef>
```

### 多炮管偏移、炮管制退与炮口火焰

入口：

```xml
<verbClass>SRA.Verb_ShootWithOffset</verbClass>
```

以及投射武器或炮塔 gun def 上：

```xml
<modExtensions>
  <li Class="SRA.ModExtension_ShootWithOffset">
    ...
  </li>
</modExtensions>
```

实现：

- `offsets` 是统一基准点列表。
- 同一 `barrelIndex` 同时驱动 projectile 出口、炮管贴图中心和炮口火焰基准。
- 对 `Building_TurretGunHasSpeed`，基准原点是建筑 `DrawPos + building.turretTopOffset`。
- 对非转速炮塔或 pawn，保留旧版 `Verb_ShootWithOffset` 偏移语义。
- 若配置 `barrelTexturePath`，会绘制独立炮管并执行制退动画。
- 若配置 `muzzleFlashTexturePath`，会在开火时绘制逐帧炮口火焰。
- 动画使用 material/mesh 缓存，避免每帧创建材质或修改共享 UV。
- 炮管与火焰 altitude offset 被限制在炮塔自身大层级中，避免跨到树、建筑等其它大层级。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `offsets` | 空 | 炮管基准点列表，`x` 左右，`y` 前后 |
| `barrelTexturePath` | null | 炮管贴图路径，不带扩展名 |
| `barrelTextureSize` | `(1,1)` | 炮管绘制尺寸 |
| `barrelUseGlowShader` | false | 炮管是否使用 glow shader |
| `barrelColor` | white | 炮管颜色乘算 |
| `barrelAltitudeOffset` | 0.05 | 炮管相对炮塔顶图层偏移 |
| `recoilAmount` | 0.5 | 最大制退距离 |
| `recoilDurationTicks` | 20 | 制退动画总 tick |
| `recoilKickTicks` | 5 | 后坐阶段 tick |
| `muzzleFlashTexturePath` | null | 炮口火焰序列帧路径 |
| `muzzleFlashDrawSize` | `(1,1)` | 单帧绘制尺寸 |
| `muzzleFlashOffset` | `(0,0)` | 火焰相对基准点局部偏移 |
| `muzzleFlashForwardOffset` | 0 | 火焰额外前移 |
| `muzzleFlashAltitudeOffset` | 0.07 | 火焰图层偏移 |
| `muzzleFlashUseGlowShader` | true | 火焰是否使用 glow shader |
| `muzzleFlashColor` | white | 火焰颜色乘算 |
| `muzzleFlashFrameCount` | 10 | 总帧数 |
| `muzzleFlashFrameColumns` | 0 | 每行列数；0 表示横向单行 |
| `muzzleFlashTicksPerFrame` | 1 | 每帧持续 tick |

示例：

```xml
<ThingDef ParentName="BaseGun">
  <defName>SRA_TurretGun_Visual</defName>
  <verbs>
    <li>
      <verbClass>SRA.Verb_ShootWithOffset</verbClass>
      <defaultProjectile>SRA_TurretBullet</defaultProjectile>
      <range>45</range>
      <burstShotCount>4</burstShotCount>
      <ticksBetweenBurstShots>8</ticksBetweenBurstShots>
    </li>
  </verbs>
  <modExtensions>
    <li Class="SRA.ModExtension_ShootWithOffset">
      <offsets>
        <li>(-0.35, 1.2)</li>
        <li>(0.35, 1.2)</li>
      </offsets>
      <barrelTexturePath>SRA/Turrets/Barrel</barrelTexturePath>
      <barrelTextureSize>(2.4, 0.45)</barrelTextureSize>
      <barrelAltitudeOffset>-0.01</barrelAltitudeOffset>
      <recoilAmount>0.35</recoilAmount>
      <recoilDurationTicks>18</recoilDurationTicks>
      <recoilKickTicks>4</recoilKickTicks>
      <muzzleFlashTexturePath>SRA/Turrets/MuzzleFlash</muzzleFlashTexturePath>
      <muzzleFlashDrawSize>(1.2, 1.2)</muzzleFlashDrawSize>
      <muzzleFlashForwardOffset>0.2</muzzleFlashForwardOffset>
      <muzzleFlashFrameCount>8</muzzleFlashFrameCount>
      <muzzleFlashFrameColumns>8</muzzleFlashFrameColumns>
      <muzzleFlashTicksPerFrame>1</muzzleFlashTicksPerFrame>
    </li>
  </modExtensions>
</ThingDef>
```

### 跨地图火炮调度

入口：

```xml
<thingClass>SRA.Building_TurretGunHasSpeed</thingClass>
<comps>
  <li Class="SRA.CompProperties_HNGT_GlobalBallisticAttack">
    ...
  </li>
</comps>
```

实现：

- 从 `HeavyNavalGunTurret` 项目迁移并整合到 Lib 的跨地图火炮系统。
- 火炮必须是玩家阵营、已生成、非口袋地图内的 `Building_TurretGunHasSpeed`。
- 所有可跨地图攻击的火炮会被 RemoteMonitoring 扫描，并按 `categoryKey` 分组成按钮。
- 按钮显示当前可调度数量和总数量，例如 `重型舰炮（8/8）`。
- 点击按钮后进入世界地图选择模式，选择 `MapParent` 后会建立/刷新 RemoteMonitoring 链接并打开地图。
- 打开地图后进入连续瞄准模式；左键每次调度一门同类别可用火炮，右键取消，或所有火炮不可用时自动结束。
- 发射端会旋转到目标世界格方向，并播放配置的若干次本地假 burst。
- 假 burst 使用 `Verb_ShootWithOffset` 的 offset，因此子弹视觉、炮管制退和炮口火焰仍保持同一炮管索引。
- 假 burst 的 projectile 使用 `ProjectileHitFlags.None`，不会在本地地图造成命中或爆炸；真正伤害由世界飞行物抵达目标地图后生成的 payload 处理。

`CompProperties_HNGT_GlobalBallisticAttack` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `cooldownSeconds` | 900 | 调度一次后的冷却秒数 |
| `iconPath` | null | RemoteMonitoring 分组按钮图标路径 |
| `categoryKey` | 建筑 defName | 分组 key，相同 key 合并为一个按钮 |
| `categoryLabelKey` | null | 类别显示名 keyed 文本；留空用建筑 label |
| `categoryDescKey` | null | 类别说明 keyed 文本；留空用通用说明 |
| `worldObjectDef` | `SRA_GlobalAttackDevice` | 世界地图飞行物 Def |
| `payloadThingDef` | null | 抵达后在目标地图生成的 payload，缺失则该火炮不启用 |

`DefModExtension_GlobalAttackDeviceParams` 字段，挂在 `WorldObjectDef.modExtensions` 上：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `flightSpeed` | 0.00025 | 世界地图飞行速度，默认值与原版运输舱一致；值越大越快抵达 |
| `remoteBurstShotCount` | 1 | 远程炮击这一轮本地 fake burst 的发数；只影响远程炮击，不改变普通射击的 `burstShotCount`。若火炮使用 `CompChangeableProjectile`，每发都会正常消耗已装填弹药；若建筑本体使用 `CompRefuelable` 且 `consumeFuelOnlyWhenUsed=true`，每发会消耗 1 点燃料。接单前会检查整轮 fake burst 的弹药/燃料是否足够，不足时按钮直接灰掉 |

`ModExtension_HighOrbitAttack` 字段，挂在 payload `ThingDef.modExtensions` 上：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `projectileDef` | null | 每次落弹使用的 projectile Def |
| `impactAreaRadius` | 15 | 落弹散布半径，也用于 RemoteMonitoring 瞄准半径预览 |
| `explosionCount` | 30 | 落弹次数 |
| `bombIntervalTicks` | 18 | 落弹间隔 tick |
| `warmupTicks` | 60 | payload 生成后到开始打击的预热 tick |
| `projectileTexturePath` | `Things/Projectile/Bullet_Big` | 空中落弹视觉贴图路径 |
| `shaderType` | null | 空中落弹视觉材质使用的 `ShaderTypeDef`；留空时沿用原版 `Transparent`，能量/发光弹头常用 `MoteGlow` |
| `drawSize` | `(2.5, 2.5)` | 空中落弹视觉绘制尺寸；默认等同原版 `BombardmentProjectile` |
| `projectileFlyTimeTicks` | 60 | 落弹视觉飞行 tick |
| `preImpactSoundVolume` | 1 | 原版预命中音效音量倍率 |
| `avoidThickRoof` | true | 优先选择非厚岩顶格 |
| `punchThroughThickRoofIfBlocked` | true | 命中厚岩顶时先移除屋顶再结算 |

示例：

```xml
<ThingDef ParentName="BuildingBase">
  <defName>SRA_HeavyNavalGunTurret</defName>
  <thingClass>SRA.Building_TurretGunHasSpeed</thingClass>
  <building>
    <turretGunDef>SRA_HeavyNavalGun</turretGunDef>
    <turretTopOffset>(0, 0)</turretTopOffset>
  </building>
  <comps>
    <li Class="SRA.CompProperties_HNGT_GlobalBallisticAttack">
      <cooldownSeconds>900</cooldownSeconds>
      <iconPath>SRA/UI/Commands/UI_SRA_NavalGunStrike</iconPath>
      <categoryKey>SRA_HeavyNavalGun</categoryKey>
      <categoryLabelKey>SRA_HeavyNavalGun_Category</categoryLabelKey>
      <worldObjectDef>SRA_HeavyNavalGun_GlobalAttackDevice</worldObjectDef>
      <payloadThingDef>SRA_HeavyNavalGun_OrbitPayload</payloadThingDef>
    </li>
  </comps>
</ThingDef>

<WorldObjectDef>
  <defName>SRA_HeavyNavalGun_GlobalAttackDevice</defName>
  <worldObjectClass>SRA.WorldObject_GlobalAttackDevice</worldObjectClass>
  <texture>World/WorldObjects/TravelingTransportPods</texture>
  <modExtensions>
    <li Class="SRA.DefModExtension_GlobalAttackDeviceParams">
      <flightSpeed>0.005</flightSpeed>
      <remoteBurstShotCount>3</remoteBurstShotCount>
    </li>
  </modExtensions>
</WorldObjectDef>

<ThingDef ParentName="OrbitalStrikeBase">
  <defName>SRA_HeavyNavalGun_OrbitPayload</defName>
  <thingClass>SRA.HighOrbitAttack</thingClass>
  <tickerType>Normal</tickerType>
  <modExtensions>
    <li Class="SRA.ModExtension_HighOrbitAttack">
      <projectileDef>SRA_HeavyNavalGun_Projectile</projectileDef>
      <impactAreaRadius>15</impactAreaRadius>
      <explosionCount>30</explosionCount>
      <bombIntervalTicks>18</bombIntervalTicks>
      <warmupTicks>60</warmupTicks>
      <projectileTexturePath>Things/Projectile/Bullet_Big</projectileTexturePath>
      <shaderType>MoteGlow</shaderType>
      <drawSize>(4, 4)</drawSize>
    </li>
  </modExtensions>
</ThingDef>
```

### 持续射击/转火

入口：

```xml
<li Class="SRA.CompProperties_SustainedShoot" />
```

实现：

- 挂在武器 ThingDef 的 `comps` 上，不需要挂在建筑上。
- 需要主 verb 是 `SRA.Verb_ShootWithOffset` 或其子类 `SRA.Verb_ShootSustained`。
- 在 burst 尚有剩余弹数时，如果当前目标死亡、倒地或不可用，会清除前摇/后摇并尝试寻找新目标继续剩余弹数。
- pawn 武器和 `Building_TurretGunHasSpeed`、`Comp_MultiTurretGun` 都有适配。
- 对转速炮塔，会等待炮塔旋转对正目标后再继续开火。

示例：

```xml
<ThingDef ParentName="BaseGun">
  <defName>SRA_SustainedGun</defName>
  <verbs>
    <li>
      <verbClass>SRA.Verb_ShootWithOffset</verbClass>
      <burstShotCount>10</burstShotCount>
      <ticksBetweenBurstShots>6</ticksBetweenBurstShots>
    </li>
  </verbs>
  <comps>
    <li Class="SRA.CompProperties_SustainedShoot" />
  </comps>
</ThingDef>
```

### 多炮塔组件

入口：

```xml
<li Class="SRA.CompProperties_MultiTurretGun">
  ...
</li>
```

实现：

- 继承原版 `CompProperties_TurretGun`。
- 可在一个建筑上挂多个炮塔 gun comp。
- `ID` 用于区分保存字段，多个 comp 必须不同。
- 支持 `CompSustainedShoot`。

字段：

| 字段 | 来源 | 作用 |
| --- | --- | --- |
| `ID` | SRALib | 保存字段后缀，必须唯一 |
| `turretDef`、`angleOffset` 等 | 原版 `CompProperties_TurretGun` | 原版炮塔组件字段 |

示例：

```xml
<li Class="SRA.CompProperties_MultiTurretGun">
  <ID>0</ID>
  <turretDef>SRA_LeftGun</turretDef>
  <angleOffset>-20</angleOffset>
</li>
<li Class="SRA.CompProperties_MultiTurretGun">
  <ID>1</ID>
  <turretDef>SRA_RightGun</turretDef>
  <angleOffset>20</angleOffset>
</li>
```

### SRA 光束 Verb

入口：

```xml
<li Class="SRA.VerbProperties_SRAShootBeam">
  <verbClass>SRA.Verb_SRAShootBeam</verbClass>
  ...
</li>
```

实现：

- 基于原版 `Verb_ShootBeam` 的 burst 光束流程，保留原版 `beamMoteDef`、`beamDamageDef`、`beamTotalDamage`、`beamWidth`、`beamCurvature`、`beamMaxDeviation`、`beamLineFleckDef`、`beamEndEffecterDef` 等字段。
- `hitRadius` 扩展当前落点命中判定范围；同一发 beam tick 内同一个 Thing 只会被伤害一次，终点/判定半径伤害优先于路径伤害。
- `targetignore` 统一控制敌我过滤，可选 `ignoreNonHostile`、`ignoreNonLOSBlockingNonHostile`、`ignoreFriendly`、`ignoreNothing`，忽略范围依次减小。
- `damageBeamPath` 开启后，发射源到当前光束落点路径上的单位也会受击；`pathHitRadius` 单独控制路径粗细。
- `penetrateObstacles` 开启后，不要求 LOS，命中点与路径不会被墙体或满填充建筑截断。
- `customTrajectory` 可按 tick 定义任意数量的相对落点，允许落点在地图外；路径枚举会在离开地图后停止，避免无限距离带来额外开销。
- `mining` 开启后，对 `Mineable` 使用采矿伤害逻辑，调用一次 `Notify_TookMiningDamage`，被摧毁时走 `DestroyMined`，产出为正常采矿产出。
- `extraDamages` 会在主光束伤害之后追加结算；路径伤害会按 `pathDamageFactor` 同步缩放额外伤害。
- 带 `VerbProperties_SRAShootBeam` 的武器/炮塔武器会自动在游戏内细则中补充光束伤害、伤害频率、光束穿甲和命中范围；若该光束是主攻击，会替换原版只读取 `beamDamageDef.defaultArmorPenetration` 的穿甲显示。

`VerbProperties_SRAShootBeam` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `beamDamageAmount` | -1 | 单次命中判定的伤害覆盖；小于 0 时使用 `beamTotalDamage` 或 `beamDamageDef.defaultDamage` |
| `beamArmorPenetration` | -1 | 光束穿甲覆盖；小于 0 时使用 `beamDamageDef.defaultArmorPenetration` |
| `extraDamages` | null | 主伤害之外追加的 `ExtraDamage` 列表；`amount` 会随当前 `damageFactor` 缩放 |
| `hitRadius` | 0 | 落点命中判定半径；0 表示只处理落点格，若原版 `beamHitsNeighborCells=true` 则保留邻格逻辑 |
| `targetignore` | `ignoreNothing` | 目标过滤策略；四档见下表 |
| `damageBeamPath` | false | 是否伤害光束路径上的单位 |
| `pathHitRadius` | 0 | 路径伤害粗细；0 表示只处理中心线格 |
| `pathDamageFactor` | 1 | 路径伤害倍率；终点/判定半径伤害仍为 1 |
| `penetrateObstacles` | false | 是否穿透障碍并忽略 LOS |
| `mining` | false | 是否对矿物使用采矿伤害产出 |
| `customTrajectory` | null | 自定义落点轨迹列表 |
| `extraBeamMoteDefs` | null | 额外维护的光束 mote 列表；每个 mote 会和主 `beamMoteDef` 一样连接发射源与当前视觉落点，适合用宽透明 shader 层替代大量沿线 fleck |

`targetignore` 可选值：

| 值 | 效果 |
| --- | --- |
| `ignoreNonHostile` | 忽略所有非敌对目标，只伤害敌对目标 |
| `ignoreNonLOSBlockingNonHostile` | 忽略友方目标，同时忽略不阻挡视线的中立目标；会伤害所有敌对目标和阻挡视线的中立目标 |
| `ignoreFriendly` | 只忽略同阵营和盟友目标；中立目标、无阵营矿物、岩墙等仍可受伤 |
| `ignoreNothing` | 不按阵营/敌我关系过滤目标 |

常用原版 beam 字段仍然可直接写在同一个 `li` 中：

| 字段 | 作用 |
| --- | --- |
| `hasStandardCommand` | 是否显示标准攻击按钮 |
| `warmupTime` | 开火前摇，单位秒 |
| `range`、`minRange` | 射程与最小射程 |
| `requireLineOfSight` | 是否要求目标 LOS；若 `penetrateObstacles=true`，本 Verb 会同时绕过 warmup 的 LOS 拒绝和实际命中截断 |
| `muzzleFlashScale` | 原版枪口闪光大小 |
| `soundCastTail`、`soundCastBeam` | 开火尾音与持续光束音 |
| `beamStartOffset` | beam mote 起点沿射击方向的偏移 |
| `beamFullWidthRange`、`beamWidth`、`beamMaxDeviation`、`beamCurvature` | 未配置 `customTrajectory` 时沿用的原版光束扫动参数 |
| `burstShotCount`、`ticksBetweenBurstShots` | 光束伤害判定次数与间隔 |
| `beamFleckChancePerTick` | 落点 fleck 每 tick 生成概率 |
| `beamDamageDef`、`beamTotalDamage` | 原版光束伤害类型和总伤害参数 |
| `beamGroundFleckDef`、`beamMoteDef`、`beamEndEffecterDef`、`beamLineFleckDef` | 地面、光束本体、落点、路径视觉效果 |
| `extraBeamMoteDefs` | 额外光束视觉层；用于叠加泛光、电离带、噪声火花等低对象数效果 |
| `beamChanceToStartFire`、`beamChanceToAttachFire`、`beamFireSizeRange` | 点燃地面、附着火焰概率和火焰大小 |
| `beamLineFleckChanceCurve` | 路径 fleck 沿光束长度的生成概率曲线 |
| `targetParams` | 原版目标参数，例如 `canTargetLocations=true` |

#### 分段平铺光束扩散 Mote

`SRA.Mote_TiledBeamSheath` 继承 `MoteDualAttached`，适合放进 `extraBeamMoteDefs`。它不会把贴图整体拉伸成一整条矩形，而是在同一个 mote 的 `DrawAt` 内沿起点到终点绘制多个短段 quad；每段按独立相位等比放大并淡出，用来模拟沿光束周围散开的火焰、电离碎片或火花带。

XML 示例：

```xml
<ThingDef ParentName="MoteBase">
  <defName>Example_TiledBeamSheath</defName>
  <thingClass>SRA.Mote_TiledBeamSheath</thingClass>
  <mote>
    <fadeInTime>0.06</fadeInTime>
    <fadeOutTime>0.18</fadeOutTime>
    <solidTime>999999</solidTime>
    <needsMaintenance>True</needsMaintenance>
    <rotateTowardsTarget>True</rotateTowardsTarget>
    <scaleToConnectTargets>True</scaleToConnectTargets>
    <fadeOutUnmaintained>True</fadeOutUnmaintained>
  </mote>
  <graphicData>
    <texPath>SRA/Mote/05fire</texPath>
    <graphicClass>Graphic_Single</graphicClass>
    <shaderType>MoteGlow</shaderType>
    <drawSize>(1, 1)</drawSize>
    <color>(0.35, 1, 1, 0.72)</color>
  </graphicData>
  <modExtensions>
    <li Class="SRA.MoteTiledBeamSheathExtension">
      <segmentSpacing>0.85</segmentSpacing>
      <baseSize>1.9</baseSize>
      <expandedSize>5.4</expandedSize>
      <minSizeFactor>0.55</minSizeFactor>
      <scrollSpeed>0.55</scrollSpeed>
      <phaseStride>0.34</phaseStride>
      <alpha>0.48</alpha>
      <outerSizeFactor>1</outerSizeFactor>
      <innerSizeFactor>0.36</innerSizeFactor>
      <sizeJitter>0.18</sizeJitter>
      <maxSegments>96</maxSegments>
    </li>
  </modExtensions>
</ThingDef>
```

`MoteTiledBeamSheathExtension` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `segmentSpacing` | 0.95 | 沿光束方向每个短段的间距 |
| `baseSize` | -1 | 每段刚出现时的基础等比尺寸；小于等于 0 时回退到旧字段 `baseWidth` |
| `expandedSize` | -1 | 每段扩散后的最大等比尺寸；小于等于 0 时回退到旧字段 `expandedWidth` |
| `minSizeFactor` | -1 | 初始尺寸相对 `baseSize` 的倍率；小于等于 0 时回退到旧字段 `minWidthFactor` |
| `scrollSpeed` | 0.22 | 相位滚动速度；越大扩散循环越快 |
| `phaseStride` | 0.31 | 相邻短段的相位错开量 |
| `alpha` | 0.55 | 整体透明度倍率，会再乘以 mote 自身淡入淡出 |
| `alphaPower` | 1.25 | 淡出曲线；越大越快消失 |
| `outerAlpha` | 1 | 外层扩散段透明度倍率 |
| `outerSizeFactor` | -1 | 外层扩散段等比尺寸倍率；小于等于 0 时回退到旧字段 `outerWidthFactor` |
| `drawInnerLayer` | true | 是否额外绘制一层更亮、更窄的内层短段 |
| `innerAlpha` | 0.8 | 内层短段透明度倍率 |
| `innerSizeFactor` | -1 | 内层短段等比尺寸倍率；小于等于 0 时回退到旧字段 `innerWidthFactor` |
| `innerWhiteBlend` | 0.7 | 内层颜色向白色混合的比例 |
| `sizeJitter` | -1 | 每段等比尺寸随机扰动；小于 0 时回退到旧字段 `widthJitter` |
| `perpendicularJitter` | 0.16 | 每段垂直于光束方向的随机偏移 |
| `altitudeOffset` | 0.01 | 内层短段相对外层的高度偏移，减少同面闪烁 |
| `maxSegments` | 96 | 最大绘制段数，用于限制超长光束的 draw call |

兼容字段 `segmentLength`、`baseWidth`、`expandedWidth`、`minWidthFactor`、`maxLengthFactor`、`outerWidthFactor`、`outerLengthFactor`、`innerWidthFactor`、`innerLengthFactor`、`widthJitter`、`lengthJitter` 仍保留，旧 XML 不会报错；新配置建议使用 `Size` 字段，避免把贴图横向或纵向拉伸。

`SRABeamTrajectoryPoint` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `offset` | `(0,0)` | 相对目标点的偏移。参考系为“从正下方向正上方目标射击”：`x` 为右侧，`y` 为射击前方 |
| `arrivalTick` | 0 | burst 开始后到达该相对落点的 tick；相邻节点间线性插值 |

示例：

```xml
<verbs>
  <li Class="SRA.VerbProperties_SRAShootBeam">
    <verbClass>SRA.Verb_SRAShootBeam</verbClass>
    <range>120</range>
    <warmupTime>1.5</warmupTime>
    <burstShotCount>60</burstShotCount>
    <ticksBetweenBurstShots>1</ticksBetweenBurstShots>
    <beamMoteDef>SRA_HeavyLaserBeam</beamMoteDef>
    <beamDamageDef>Burn</beamDamageDef>
    <beamArmorPenetration>8</beamArmorPenetration>
    <beamTotalDamage>180</beamTotalDamage>
    <hitRadius>1.4</hitRadius>
    <targetignore>ignoreNonLOSBlockingNonHostile</targetignore>
    <damageBeamPath>true</damageBeamPath>
    <pathHitRadius>0.6</pathHitRadius>
    <pathDamageFactor>0.5</pathDamageFactor>
    <penetrateObstacles>true</penetrateObstacles>
    <mining>true</mining>
    <customTrajectory>
      <li>
        <offset>(0,-20)</offset>
        <arrivalTick>0</arrivalTick>
      </li>
      <li>
        <offset>(0,0)</offset>
        <arrivalTick>30</arrivalTick>
      </li>
      <li>
        <offset>(0,120)</offset>
        <arrivalTick>60</arrivalTick>
      </li>
    </customTrajectory>
  </li>
</verbs>
```

### 近战 AOE

入口：

```xml
<verbClass>SRA.Verb_MeleeAttackDamage_AOE</verbClass>
```

在 `ManeuverDef.modExtensions` 上配置：

```xml
<li Class="SRA.MeleeAttackAOE_Extension">
  ...
</li>
```

实现：

- 在主目标方向形成扇形范围。
- 命中主目标外，额外选择扇形内敌对 pawn。
- `extraAccuracy` 加到命中率，`extraTracking` 从目标闪避率中扣除。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `angle` | 100 | 扇形角度 |
| `radius` | 1.7 | 半径 |
| `maxHitTarget` | 3 | 最大命中目标数，包含主目标 |
| `extraAccuracy` | 0 | 额外命中率 |
| `extraTracking` | 0 | 额外追踪，降低闪避 |

可选特效：

```xml
<li Class="SRA.Effecter_Extension">
  <effcterDef>SRA_MeleeSlashEffect</effcterDef>
</li>
```

示例：

```xml
<ManeuverDef>
  <defName>SRA_SweepingStrike</defName>
  <modExtensions>
    <li Class="SRA.MeleeAttackAOE_Extension">
      <angle>120</angle>
      <radius>2.4</radius>
      <maxHitTarget>4</maxHitTarget>
      <extraAccuracy>0.1</extraAccuracy>
      <extraTracking>0.15</extraTracking>
    </li>
  </modExtensions>
</ManeuverDef>
```

## 爆炸、伤害与器官命中

### 多重爆炸投射物

入口：

```xml
<thingClass>SRA.Projectile_MultiExplosive</thingClass>
```

以及 projectile ThingDef 上：

```xml
<modExtensions>
  <li Class="SRA.MultiExplosiveExtension">
    ...
  </li>
</modExtensions>
```

实现：

- 投射物命中后，可按列表执行多个爆炸。
- 可额外发射若干子弹头。
- 强制偏移沿用原版 verb 配置：在发射武器的 `<verbs>` 条目中写 `<forcedMissRadius>`。`Projectile_MultiExplosive` 会使用 verb 已经偏移后的实际落点。
- 原版会要求带 `forcedMissRadius` 的 verb 使用“爆炸投射物”。`Projectile_MultiExplosive` 默认不继承 `Projectile_Explosive`，所以需要在带 forced miss 的 projectile Def 上添加 `SRA.CompProperties_ForcedMissExplosionMarker`，只作为无副作用识别标记。
- 可配置敌我识别、障碍穿透、采矿爆炸、爆炸前后生成物、爆炸前后处理效果。
- 若 `preNotifyEffects` 或 `postNotifyEffects` 非空，会改用 `ExplosionWithProcessing`；否则走原版 `GenExplosion.DoExplosion`。
- `Projectile_MultiExplosive_beam` 使用同一套 `MultiExplosionProperties` 字段，但扩展名是 `MultiExplosive_BeamExtension`；它继承原版 `Beam`，不走普通爆炸投射物识别路径，通常不要在 beam verb 上配置 `forcedMissRadius`。
- Beam 版本可在 `MultiExplosive_BeamExtension` 上额外配置 `hitRadius` 与 `damageBeamPath`。这些额外命中的伤害使用 projectile 自身的 `damageDef`、`damageAmountBase`、`armorPenetrationBase` 和 `extraDamages`，不是 `MultiExplosive_Beams` 内的爆炸伤害。
- Beam 版本默认只在主落点执行 `MultiExplosive_Beams`；若要在 hitRadius/pathDamage 产生的额外判定格上也执行爆炸，需要显式开启 `applyExplosionToExtraHitCells`。

`MultiExplosiveExtension` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `multiexplosions` | 空 | 命中后执行的爆炸列表 |
| `bulletLaunches` | 空 | 命中后额外发射的 projectile 列表 |

`MultiExplosive_BeamExtension` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `hitRadius` | 0 | 落点额外判定半径；0 时不额外扫描落点格，保留原版 Beam 只命中一个目标的行为 |
| `targetignore` | `ignoreNothing` | 光束本体额外伤害的目标过滤策略；可选值同 `Verb_SRAShootBeam.targetignore` |
| `damageBeamPath` | false | 是否让发射源到主落点之间的路径单位受到光束本体伤害 |
| `pathHitRadius` | 0 | 路径伤害粗细；0 表示只处理中心线格 |
| `pathDamageFactor` | 1 | 路径伤害倍率；落点额外判定半径内伤害仍为 1 |
| `penetrateObstacles` | false | 光束本体的 hitRadius/pathDamage 是否忽略 LOS 和墙体截断；不影响单个爆炸条目的 `penetrateObstacles` |
| `applyExplosionToExtraHitCells` | false | 是否在 hitRadius/pathDamage 产生的额外判定格上也执行 `MultiExplosive_Beams`；主落点始终按旧逻辑爆炸 |
| `MultiExplosive_Beams` | 空 | 主落点执行的爆炸列表；若 `applyExplosionToExtraHitCells=true`，也会在额外判定格上执行 |

发射武器的原版强制偏移写法：

```xml
<verbs>
  <li>
    <verbClass>Verb_LaunchProjectile</verbClass>
    <defaultProjectile>SRA_MultiExplosiveShell</defaultProjectile>
    <forcedMissRadius>8</forcedMissRadius>
  </li>
</verbs>
```

对应 projectile Def 需要添加爆炸识别 marker，否则原版 Def 校验会报 `incorrect forcedMiss settings`：

```xml
<comps>
  <li Class="SRA.CompProperties_ForcedMissExplosionMarker" />
</comps>
```

不要把这个 marker 加到不使用 `forcedMissRadius` 的 projectile Def 上；原版校验同样会要求被识别为爆炸投射物的 verb 配置 forced miss。

`MultiExplosionProperties` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `radius` | 0 | 爆炸半径 |
| `damageDef` | null | 伤害类型 |
| `damageAmount` | 1 | 伤害 |
| `armorPenetration` | 1 | 穿甲 |
| `explosionSound` | null | 爆炸声音 |
| `explosionDamageFalloff` | true | 是否按距离衰减 |
| `explosionEffect` | null | 额外 effecter |
| `explosionEffectLifetimeTicks` | 0 | effecter 维持 tick |
| `onlyAntiHostile` | false | 非敌对目标加入 ignoredThings |
| `penetrateObstacles` | false | 爆炸格不被墙或满填充建筑阻挡 |
| `mining` | false | 对矿物使用采矿伤害结算，调用 `Notify_TookMiningDamage` 两次，约 200% 采矿效率 |
| `preExplosionSpawnThingDef` | null | 每个爆炸格影响前概率生成物 |
| `preExplosionSpawnChance` | 0 | 每格前置生成概率 |
| `preExplosionSpawnThingCount` | 1 | 每格前置生成数量 |
| `preExplosionSpawnSingleThingDef` | null | 爆炸开始时在中心生成单个物体 |
| `postExplosionSpawnThingDef` | null | 每个爆炸格影响后概率生成物 |
| `postExplosionSpawnChance` | 0 | 每格后置生成概率 |
| `postExplosionSpawnThingCount` | 1 | 每格后置生成数量 |
| `postExplosionSpawnSingleThingDef` | null | 爆炸结束时在中心生成单个物体 |
| `postExplosionGasType` | null | 爆炸后气体类型 |
| `postExplosionGasRadiusOverride` | null | 气体半径覆写 |
| `postExplosionGasAmount` | 255 | 气体量 |
| `preNotifyEffects` | 空 | `Notify_Explosion` 前效果列表 |
| `postNotifyEffects` | 空 | `Notify_Explosion` 后效果列表 |

`BulletLaunchProperties` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `projectileDef` | null | 额外发射的 projectile |
| `bulletCount` | 1 | 数量 |
| `angleRange` | 60 | 相对母弹方向左右随机角度范围 |
| `distanceRange` | `3~10` | 随机目标距离范围 |

示例：

```xml
<ThingDef ParentName="BaseBullet">
  <defName>SRA_MultiExplosiveShell</defName>
  <thingClass>SRA.Projectile_MultiExplosive</thingClass>
  <projectile>
    <damageDef>Bomb</damageDef>
    <damageAmountBase>10</damageAmountBase>
    <speed>40</speed>
  </projectile>
  <comps>
    <li Class="SRA.CompProperties_ForcedMissExplosionMarker" />
  </comps>
  <modExtensions>
    <li Class="SRA.MultiExplosiveExtension">
      <multiexplosions>
        <li>
          <radius>4.9</radius>
          <damageDef>Bomb</damageDef>
          <damageAmount>50</damageAmount>
          <armorPenetration>0.25</armorPenetration>
          <penetrateObstacles>true</penetrateObstacles>
        </li>
        <li>
          <radius>2.9</radius>
          <damageDef>Flame</damageDef>
          <damageAmount>15</damageAmount>
          <postExplosionSpawnThingDef>Filth_Fuel</postExplosionSpawnThingDef>
          <postExplosionSpawnChance>0.25</postExplosionSpawnChance>
        </li>
      </multiexplosions>
      <bulletLaunches>
        <li>
          <projectileDef>SRA_Submunition</projectileDef>
          <bulletCount>6</bulletCount>
          <angleRange>80</angleRange>
          <distanceRange>5~12</distanceRange>
        </li>
      </bulletLaunches>
    </li>
  </modExtensions>
</ThingDef>
```

Beam 版本示例：

```xml
<ThingDef ParentName="BaseBeam">
  <defName>SRA_MultiExplosiveBeam</defName>
  <thingClass>SRA.Projectile_MultiExplosive_Beam</thingClass>
  <projectile>
    <damageDef>Burn</damageDef>
    <damageAmountBase>30</damageAmountBase>
    <armorPenetrationBase>0.5</armorPenetrationBase>
  </projectile>
  <modExtensions>
    <li Class="SRA.MultiExplosive_BeamExtension">
      <hitRadius>1.5</hitRadius>
      <targetignore>ignoreNonLOSBlockingNonHostile</targetignore>
      <damageBeamPath>true</damageBeamPath>
      <pathHitRadius>0.5</pathHitRadius>
      <pathDamageFactor>0.5</pathDamageFactor>
      <penetrateObstacles>true</penetrateObstacles>
      <applyExplosionToExtraHitCells>false</applyExplosionToExtraHitCells>
      <MultiExplosive_Beams>
        <li>
          <radius>3.9</radius>
          <damageDef>EMP</damageDef>
          <damageAmount>30</damageAmount>
          <mining>true</mining>
        </li>
      </MultiExplosive_Beams>
    </li>
  </modExtensions>
</ThingDef>
```

### 复合爆炸投射物

入口：

```xml
<thingClass>SRA.Projectile_CompoundExplosion</thingClass>
```

并让 projectile 属性使用：

```xml
<projectile Class="SRA.ProjectileProperties_CompoundExplosion">
  ...
</projectile>
```

实现：

- 从 `HeavyNavalGunTurret` 迁移的简单复合爆炸 projectile。
- 命中后先执行 projectile 自身的主爆炸，再按 `additionalExplosions` 追加额外爆炸。
- 比 `Projectile_MultiExplosive` 轻量，但扩展能力也更少；新复杂爆炸优先使用 `Projectile_MultiExplosive`。

`ExplosionParams` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `damageDef` | null | 额外爆炸伤害类型，缺失则跳过该条 |
| `radius` | 0 | 额外爆炸半径 |
| `damageAmount` | -1 | 额外爆炸伤害 |
| `armorPenetration` | -1 | 额外爆炸穿甲 |
| `soundExplode` | null | 额外爆炸音效 |

示例：

```xml
<ThingDef ParentName="BaseBullet">
  <defName>SRA_CompoundShell</defName>
  <thingClass>SRA.Projectile_CompoundExplosion</thingClass>
  <projectile Class="SRA.ProjectileProperties_CompoundExplosion">
    <damageDef>Bomb</damageDef>
    <damageAmountBase>80</damageAmountBase>
    <explosionRadius>4.9</explosionRadius>
    <speed>35</speed>
    <additionalExplosions>
      <li>
        <damageDef>Flame</damageDef>
        <radius>6.9</radius>
        <damageAmount>20</damageAmount>
      </li>
    </additionalExplosions>
  </projectile>
</ThingDef>
```

### 核光焰伤害类型

入口：

```xml
<workerClass>SRA.DamageWorker_NuclearFlame</workerClass>
```

兼容入口：

```xml
<workerClass>Verse.DamageWorker_NuclearFlame</workerClass>
```

实现：

- 基于 `DamageWorker_AddInjury`。
- 对血肉 pawn 造成的新伤口会尝试转为永久伤。
- 有概率附着火焰；爆炸影响格也会尝试点火。
- 非 pawn 物体被摧毁时会在占用格生成灰烬。
- 若存在 `NuclearFlameWave` EffecterDef，爆炸开始时播放该效果；缺失时静默跳过。

### ExplosionWithProcessing

入口：

- 自动入口：`MultiExplosionProperties.preNotifyEffects` 或 `postNotifyEffects` 非空。
- ThingDef 入口：`SRA_ExplosionWithProcessing`，thingClass 为 `SRA.ExplosionWithProcessing`。

实现：

- 继承原版 `Explosion`。
- 在逐格爆炸扩散时执行前置/后置处理，避免“爆炸扩散到之前就先触发效果”。
- 只对爆炸实际影响格内目标执行效果。
- 使用池化列表和 HashSet，减少爆炸时 GC。

当前内置效果：`SRA.ExplosionNotifyEffect_Hediff`

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `onlyAffectedCells` | true | 仅处理爆炸实际影响格 |
| `skipIgnoredThings` | true | 跳过 ignoredThings |
| `hediffDef` | null | 直接施加 hediff。留空则按 damageDef 推导伤口 |
| `damageDef` | null | 用于推导伤口类型；留空用爆炸伤害类型 |
| `capacitySourceTags` | 空 | 用 `BodyPartTagDef` 指定目标器官 |
| `capacities` | 空 | 用 `PawnCapacityDef` 映射到能力来源器官 |
| `applyToWholeBody` | true | 无器官过滤时施加全身 hediff |
| `applyToAllMatchingParts` | false | 对所有匹配器官施加 |
| `skipPawnWhenNoTargetPart` | true | 无匹配器官时跳过 pawn |
| `applyToDeadPawns` | false | 是否作用于已死 pawn |
| `fixedSeverity` | 1 | 固定严重度 |
| `severityFromExplosionDamage` | false | 严重度是否来自爆炸伤害 |
| `severityPerDamage` | 1 | 伤害到严重度倍率 |
| `severityOffset` | 0 | 严重度偏移 |
| `fixedDurationTicks` | -1 | 固定持续时间；小于 0 不设置 |
| `durationFromExplosionDamage` | false | 持续时间是否来自爆炸伤害 |
| `durationTicksPerDamage` | 0 | 伤害到持续时间倍率 |
| `durationTicksOffset` | 0 | 持续时间偏移 |
| `destroysBodyParts` | true | 伤口是否可摧毁部位 |

示例：爆炸后给意识来源器官写入伤口

```xml
<preNotifyEffects>
  <li Class="SRA.ExplosionNotifyEffect_Hediff">
    <damageDef>Burn</damageDef>
    <capacities>
      <li>Consciousness</li>
    </capacities>
    <severityFromExplosionDamage>true</severityFromExplosionDamage>
    <severityPerDamage>0.25</severityPerDamage>
    <applyToAllMatchingParts>false</applyToAllMatchingParts>
    <skipPawnWhenNoTargetPart>true</skipPawnWhenNoTargetPart>
  </li>
</preNotifyEffects>
```

示例：爆炸后添加限时 hediff

```xml
<postNotifyEffects>
  <li Class="SRA.ExplosionNotifyEffect_Hediff">
    <hediffDef>SRA_ShockAftereffect</hediffDef>
    <fixedSeverity>0.5</fixedSeverity>
    <fixedDurationTicks>2500</fixedDurationTicks>
    <applyToWholeBody>true</applyToWholeBody>
  </li>
</postNotifyEffects>
```

### 无承伤倍率伤害与器官定向

入口：

```xml
<workerClass>SRA.DamageWorker_AddInjury_NoDamageFactor</workerClass>
```

可选扩展：

```xml
<li Class="SRA.DamageWorker_NoDamageFactor_Extension">
  ...
</li>
```

实现：

- 基于原版 `DamageWorker_AddInjury` 逻辑改写。
- `penetrationFactor` 用于穿透 `IncomingDamageFactor` 小于 1 的减伤。
- `organDamage` 仅用于让常规伤害选中能力来源器官，不再绕过 `ApplyDamage`。
- 若配置了器官目标且找不到目标器官，可选择跳过本次 pawn 伤害。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `penetrationFactor` | 0 | 对 `IncomingDamageFactor < 1` 的穿透比例。0 不穿透，1 完全穿透 |
| `organDamage` | null | 器官定向配置。不写则不启用 |

`DirectOrganDamageProperties` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `capacitySourceTags` | 空 | 直接指定器官 tag，如 `ConsciousnessSource` |
| `capacities` | 空 | 通过能力映射器官来源，如 `Consciousness`、`BloodPumping` |
| `skipPawnWhenNoTargetPart` | true | 有过滤但无目标器官时跳过 pawn 伤害 |
| `allowNonPawnDamage` | true | 非 pawn 目标是否仍受普通伤害 |
| `preventDamagePropagation` | true | 是否禁止常规伤害扩散到非目标部位 |

示例：

```xml
<DamageDef>
  <defName>SRA_NerveBurn</defName>
  <workerClass>SRA.DamageWorker_AddInjury_NoDamageFactor</workerClass>
  <defaultDamage>20</defaultDamage>
  <armorCategory>Heat</armorCategory>
  <modExtensions>
    <li Class="SRA.DamageWorker_NoDamageFactor_Extension">
      <penetrationFactor>0.5</penetrationFactor>
      <organDamage>
        <capacities>
          <li>Consciousness</li>
        </capacities>
        <preventDamagePropagation>true</preventDamagePropagation>
        <skipPawnWhenNoTargetPart>true</skipPawnWhenNoTargetPart>
      </organDamage>
    </li>
  </modExtensions>
</DamageDef>
```

### 抛射物拖尾

入口：

```xml
<li Class="SRA.TailBulletDef">
  ...
</li>
```

实现：

- 当前主要被 `Projectile_MultiExplosive` 使用。
- 投射物移动时按配置间隔生成 fleck 拖尾。

可在 projectile `modExtensions` 上配置，字段以 `TailBulletDef` 源码为准。

## 事件系统

入口：

```xml
<SRA.EventDef>
  ...
</SRA.EventDef>
```

实现：

- 自定义事件窗口系统。
- 支持头像、角色名、背景图、多描述文本、条件描述、选项、即时效果、关闭效果、隐藏窗口。
- `hiddenWindow=true` 时，不显示 UI，会把 `immediateEffects` 合并到 `dismissEffects` 执行。
- 支持延迟打开其它 EventDef。
- 支持世界组件 `EventVariableManager` 持久化变量。

`EventDef` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `portraitPath` | null | 头像贴图路径 |
| `characterName` | null | 角色名 |
| `doCloseXButton` | true | 是否显示关闭 X |
| `descriptions` | null | 描述文本列表 |
| `descriptionMode` | `Random` | `Random` 或 `Sequential` |
| `hiddenWindow` | false | 是否隐藏窗口，只执行效果 |
| `windowSize` | `(0,0)` | 窗口大小覆写 |
| `windowType` | `SRA.Dialog_CustomDisplay` | 窗口类型 |
| `options` | null | 选项列表 |
| `backgroundImagePath` | null | 背景图路径 |
| `immediateEffects` | null | 打开时执行 |
| `dismissEffects` | null | 关闭时执行 |
| `conditionalDescriptions` | null | 条件描述 |
| `eventUIConfig` | null | UI 配置 Def |

`EventOption` 字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `label` | null | 按钮文本 |
| `optionEffects` | null | 点击后效果 |
| `conditions` | null | 可用条件 |
| `disabledReason` | null | 禁用原因 |
| `hideWhenDisabled` | false | 不满足条件时隐藏 |

条件类型：

| Class | 字段 | 作用 |
| --- | --- | --- |
| `SRA.Condition_VariableEquals` | `name`、`value`、`valueVariableName` | 变量等于 |
| `SRA.Condition_VariableNotEqual` | `name`、`value`、`valueVariableName` | 变量不等于 |
| `SRA.Condition_VariableGreaterThan` | `name`、`value`、`valueVariableName` | 变量大于 |
| `SRA.Condition_VariableLessThan` | `name`、`value`、`valueVariableName` | 变量小于 |
| `SRA.Condition_VariableGreaterThanOrEqual` | `name`、`value`、`valueVariableName` | 变量大于等于 |
| `SRA.Condition_VariableLessThanOrEqual` | `name`、`value`、`valueVariableName` | 变量小于等于 |
| `SRA.Condition_FactionExists` | `factionDef` | 世界中存在某 faction |
| `SRA.Condition_HasThing` | `thingDef`、`count` | 当前地图资源计数满足数量 |
| `SRA.Condition_HasResearchProject` | `researchProject` | 科技已完成 |

效果类型：

| Class | 字段 | 作用 |
| --- | --- | --- |
| `SRA.Effect_OpenCustomUI` | `defName`、`delayTicks` | 打开另一个 EventDef |
| `SRA.Effect_CloseDialog` | 无 | 关闭当前窗口 |
| `SRA.Effect_ShowMessage` | `message`、`messageTypeDef` | 显示消息，`message` 会 Translate |
| `SRA.Effect_FireIncident` | `incident` | 强制触发事件 |
| `SRA.Effect_ChangeFactionRelation` | `faction`、`goodwillChange` | 改变玩家与 faction 关系 |
| `SRA.Effect_SetVariable` | `name`、`value`、`type`、`forceSet` | 设置变量，类型为 Int/Float/String/Bool |
| `SRA.Effect_ChangeFactionRelation_FromVariable` | `faction`、`goodwillVariableName` | 用变量调整关系 |
| `SRA.Effect_SpawnPawnAndStore` | `kindDef`、`count`、`storeAs` | 生成 pawn 并存变量 |
| `SRA.Effect_GiveThing` | `thingDef`、`count` | 空投物品 |
| `SRA.Effect_TakeThing` | `thingDef`、`count` | 从当前地图移除资源 |
| `SRA.Effect_SpawnPawn` | `kindDef`、`count`、`joinPlayerFaction`、`letterLabel`、`letterText`、`letterDef` | 生成 pawn |
| `SRA.Effect_SpawnSkyfaller` | `skyfaller`、`useTradeDropSpot` | 生成 skyfaller |
| `SRA.Effect_SpawnOrbitTrader` | `traderKindDef` | 添加轨道商船 |
| `SRA.Effect_ModifyVariable` | `name`、`value`、`valueVariableName`、`operation` | 变量加减乘除 |
| `SRA.Effect_ClearVariable` | `name` | 清除变量 |
| `SRA.Effect_AddQuest` | `quest` | 添加任务 |
| `SRA.Effect_FinishResearch` | `research` | 完成科技 |
| `SRA.Effect_TriggerRaid` | `points`、`faction`、`raidStrategy`、`raidArrivalMode`、`groupKind`、`pawnGroupMakers`、`letterLabel`、`letterText` | 触发袭击 |
| `SRA.Effect_CheckFactionGoodwill` | `factionDef`、`variableName` | 保存关系到变量 |
| `SRA.Effect_StoreRealPlayTime` | `variableName` | 保存真实游玩时间 |
| `SRA.Effect_StoreTicksPassed` | `variableName` | 保存游戏 tick |
| `SRA.Effect_StoreDaysPassed` | `variableName` | 保存游戏天数 |
| `SRA.Effect_StoreColonyWealth` | `variableName` | 保存殖民地财富 |

示例：

```xml
<SRA.EventDef>
  <defName>SRA_TestEvent</defName>
  <characterName>SRA_TestCharacterName</characterName>
  <portraitPath>SRA/Events/Portraits/SRA_test</portraitPath>
  <backgroundImagePath>SRA/Events/Bg/SRA_event_diplomacy_background</backgroundImagePath>
  <descriptions>
    <li>SRA_TestEvent_DescA</li>
    <li>SRA_TestEvent_DescB</li>
  </descriptions>
  <descriptionMode>Random</descriptionMode>
  <options>
    <li>
      <label>SRA_TestEvent_OptionAccept</label>
      <conditions>
        <li Class="SRA.Condition_HasResearchProject">
          <researchProject>Microelectronics</researchProject>
        </li>
      </conditions>
      <optionEffects>
        <li>
          <effects>
            <li Class="SRA.Effect_GiveThing">
              <thingDef>ComponentIndustrial</thingDef>
              <count>10</count>
            </li>
            <li Class="SRA.Effect_SetVariable">
              <name>SRA_TestAccepted</name>
              <value>true</value>
              <type>Bool</type>
            </li>
          </effects>
        </li>
      </optionEffects>
    </li>
  </options>
</SRA.EventDef>
```

### EventUIConfigDef

入口：

```xml
<SRA.EventUIConfigDef>
  ...
</SRA.EventUIConfigDef>
```

实现：

- 控制事件 UI 的默认背景、字体、窗口尺寸和布局尺寸。
- 当前内置示例为 `SRA_EventUIConfig`。

示例：

```xml
<SRA.EventUIConfigDef>
  <defName>SRA_EventUIConfig_Custom</defName>
  <labelFont>Small</labelFont>
  <drawBorders>false</drawBorders>
  <showDefName>false</showDefName>
  <showLabel>true</showLabel>
  <defaultBackgroundImagePath>SRA/Events/Bg/SRA_event_diplomacy_background</defaultBackgroundImagePath>
  <portraitSize>(1200, 800)</portraitSize>
  <nameSize>(650, 100)</nameSize>
  <textSize>(650, 350)</textSize>
  <optionsWidth>300</optionsWidth>
  <defaultWindowSize>(1600, 900)</defaultWindowSize>
</SRA.EventUIConfigDef>
```

### QuestNode 打开事件窗口

入口：

```xml
<li Class="SRA.QuestNode_EventLetter">
  <inSignal>...</inSignal>
  <eventDefName>SRA_TestEvent</eventDefName>
</li>
```

实现：

- 在 Quest 信号触发时打开指定 `EventDef`。
- 另有 `QuestNode_Root_EventLetter` 和 `QuestNode_WriteToEventVariablesWithAdd` 用于任务链集成和变量写入。

## 飞越与支援系统

这一组接口主要服务于飞越物体、空袭、飞船炮击和区域监视。字段较多，建议复制现有子 mod Def 后调整。

### 飞越物体

入口：

```xml
<thingClass>SRA.FlyOver</thingClass>
```

实现：

- 一个在地图上从起点飞到终点的 Thing。
- 支持飞行速度、高度、淡入淡出、进场偏移、阴影、伴飞、内容物落地生成。
- 可通过能力组件 `CompProperties_AbilitySpawnFlyOver` 生成。

阴影扩展：

```xml
<li Class="SRA.FlyOverShadowExtension">
  <useCustomShadow>true</useCustomShadow>
  <customShadowPath>SRA/FlyOver/Shadow</customShadowPath>
  <shadowIntensity>0.6</shadowIntensity>
  <ActuallyHeight>150</ActuallyHeight>
</li>
```

常用字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `customShadowPath` | null | 自定义阴影贴图 |
| `shadowIntensity` | 0.6 | 阴影强度 |
| `useCustomShadow` | false | 是否使用自定义阴影 |
| `minShadowAlpha` | 0.05 | 最小阴影透明度 |
| `maxShadowAlpha` | 0.2 | 最大阴影透明度 |
| `minShadowScale` | 0.5 | 最小阴影缩放 |
| `maxShadowScale` | 1 | 最大阴影缩放 |
| `ActuallyHeight` | 150 | 视觉高度 |
| `useApproachAnimation` | true | 是否启用进场动画 |
| `approachDuration` | 1 | 进场时长 |
| `approachOffsetDistance` | 3 | 进场偏移距离 |

### 能力生成飞越

入口：

```xml
<li Class="SRA.CompProperties_AbilitySpawnFlyOver">
  ...
</li>
```

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `flyOverDef` | null | 生成的 FlyOver ThingDef |
| `flightSpeed` | 1 | 飞行速度 |
| `altitude` | 15 | 飞行高度 |
| `spawnContents` | false | 是否携带内容物 |
| `contents` | null | 内容物列表 |
| `dropContentsOnImpact` | true | 终点是否投放内容物 |
| `customSound` | null | 自定义飞越音效 |
| `playFlyOverSound` | true | 是否播放声音 |
| `flyOverDistance` | 30 | 自定义终点距离 |
| `enableGroundStrafing` | false | 启用地面扫射 |
| `strafeWidth` | 3 | 扫射预览宽度 |
| `strafeLength` | 15 | 扫射长度 |
| `strafeFireChance` | 0.7 | 扫射开火概率 |
| `minStrafeProjectiles` | -1 | 最小扫射 projectile 数 |
| `maxStrafeProjectiles` | -1 | 最大扫射 projectile 数 |
| `strafeProjectile` | null | 扫射 projectile |
| `showStrafePreview` | true | 显示扫射预览 |
| `enableSectorSurveillance` | false | 启用扇区监视 |
| `showSectorPreview` | true | 显示扇区预览 |

示例：

```xml
<li Class="SRA.CompProperties_AbilitySpawnFlyOver">
  <flyOverDef>SRA_StrikeFlyOver</flyOverDef>
  <flightSpeed>1.5</flightSpeed>
  <altitude>20</altitude>
  <enableGroundStrafing>true</enableGroundStrafing>
  <strafeProjectile>SRA_StrafeBullet</strafeProjectile>
  <strafeLength>20</strafeLength>
  <strafeFireChance>0.8</strafeFireChance>
</li>
```

### 机库与空袭消耗

机库入口：

```xml
<li Class="SRA.CompProperties_AircraftHangar">
  ...
</li>
```

能力消耗入口：

```xml
<li Class="SRA.CompProperties_AircraftStrike">
  ...
</li>
```

实现：

- 机库起飞后向世界组件注册可用战机，然后建筑销毁。
- 能力使用时检查并消耗指定类型战机，消耗后进入冷却。

机库字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `aircraftDef` | null | 注册的战机 ThingDef |
| `aircraftCount` | 1 | 注册数量 |
| `skyfallerLeaving` | null | 起飞视觉 skyfaller |

空袭字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `requiredAircraftType` | null | 需要的战机类型 |
| `aircraftCooldownTicks` | 60000 | 消耗后的冷却 tick |
| `aircraftsPerUse` | 1 | 每次消耗数量 |

### 飞船炮击

入口：

```xml
<li Class="SRA.CompProperties_ShipArtillery">
  ...
</li>
```

实现：

- 挂在 FlyOver 上。
- 按间隔选择目标区域，预热后持续生成 skyfaller 炮弹。
- 可避开玩家资产、避开飞越物体自身、发送信件。

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `ticksBetweenAttacks` | 600 | 攻击间隔 |
| `attackDurationTicks` | 1800 | 攻击持续时间 |
| `warmupTicks` | 120 | 预热时间 |
| `continuousAttack` | false | 是否持续攻击到飞越结束 |
| `attackRadius` | 15 | 目标区域半径 |
| `targetOffset` | `(0,0,0)` | 目标中心偏移 |
| `avoidPlayerAssets` | true | 是否避开玩家资产 |
| `playerAssetAvoidanceRadius` | 5 | 玩家资产避让半径 |
| `ignoreProtectionChance` | 0 | 无视保护概率 |
| `skyfallerDef` | null | 单一炮弹 skyfaller |
| `skyfallerDefs` | null | 多炮弹列表 |
| `shellsPerVolley` | 1 | 每轮炮弹数 |
| `useDifferentShells` | false | 多炮弹列表是否随机 |
| `attackSound` | null | 攻击声音 |
| `warmupEffect` | null | 预热 effecter |
| `attackEffect` | null | 攻击 effecter |
| `avoidHittingFlyOver` | true | 避免击中自身 |
| `sendAttackLetter` | false | 是否发送信件 |
| `customLetterLabel` | null | 信件标题 |
| `customLetterText` | null | 信件正文 |

### 飞越空投

入口：

```xml
<li Class="SRA.CompProperties_FlyOverDropPods">
  ...
</li>
```

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `dropProgress` | 0.5 | 飞越进度达到该值时投放 |
| `useCyclicDrops` | false | 是否循环投放 |
| `cyclicDropIntervalHours` | 24 | 循环间隔小时 |
| `waitForExternalSignal` | false | 是否等待外部信号 |
| `externalSignalTag` | null | 外部信号 tag |
| `dropCount` | 1 | 投放次数 |
| `scatterRadius` | 3 | 散布半径 |
| `useTradeDropSpot` | false | 使用贸易投放点 |
| `allowFogged` | false | 允许雾区 |
| `dropAllInSamePod` | false | 所有内容同 pod |
| `leaveSlag` | false | 是否留下残骸 |
| `thingDefs` | 空 | 物品列表 |
| `dropAllContents` | false | 投放 flyover 容器全部内容 |
| `pawnKinds` | 空 | pawn 类型与数量 |
| `pawnFactionDef` | null | pawn 派系 |
| `generatePawnsOnDrop` | true | 投放时生成 pawn |
| `joinPlayer` | false | pawn 加入玩家 |
| `makePrisoners` | false | pawn 作为囚犯 |
| `assignAssaultLordJob` | false | 分配袭击 LordJob |
| `sendStandardLetter` | true | 发送标准信件 |
| `customLetterText` | null | 自定义信件文本 |
| `customLetterLabel` | null | 自定义信件标题 |

### 地面扫射

入口：

```xml
<li Class="SRA.CompProperties_GroundStrafing">
  ...
</li>
```

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `projectileDef` | null | 发射 projectile |
| `range` | 15 | 射程 |
| `lateralOffsetDistance` | 2 | 横向偏移距离 |
| `lateralOffsetMode` | `Alternating` | 横向偏移模式 |
| `longitudinalOffsetMode` | `Alternating` | 纵向偏移模式 |
| `spawnOffsetEffect` | false | 是否生成偏移特效 |
| `offsetEffectDef` | null | 偏移特效 ThingDef |

横向模式：`Fixed`、`Alternating`、`Progressive`、`Random`。

纵向模式：`Fixed`、`Alternating`、`Progressive`、`Random`、`Sinusoidal`。

### 扇区监视

入口：

```xml
<li Class="SRA.CompProperties_SectorSurveillance">
  ...
</li>
```

实现：

- 挂在 FlyOver 上。
- 在飞行方向前方扇区内寻找敌对 pawn，并发射 projectile。
- 支持最大弹药、横向/纵向发射偏移、偏移特效。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `projectileDef` | null | 发射 projectile |
| `sectorAngle` | 90 | 扇区角度 |
| `sectorRange` | 25 | 扇区距离 |
| `shotCount` | 3 | 每轮射击数 |
| `shotInterval` | 0.3 | 射击间隔秒 |
| `maxProjectiles` | -1 | 最大弹药，-1 无限 |
| `lateralOffsetDistance` | 2 | 横向偏移距离 |
| `lateralOffsetMode` | `Alternating` | 横向偏移模式 |
| `longitudinalOffsetMode` | `Alternating` | 纵向偏移模式 |
| `spawnOffsetEffect` | false | 是否生成偏移特效 |
| `offsetEffectDef` | null | 偏移特效 |

### 伴飞

入口：

```xml
<li Class="SRA.CompProperties_FlyOverEscort">
  ...
</li>
```

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `escortFlyOverDef` | null | 单一伴飞 ThingDef |
| `escortFlyOverDefs` | null | 多个伴飞 ThingDef |
| `spawnIntervalTicks` | 600 | 生成间隔 |
| `maxEscorts` | 3 | 最大伴飞数 |
| `spawnCount` | 1 | 每次生成数 |
| `spawnDistance` | 10 | 距主飞行物生成距离 |
| `lateralOffset` | 5 | 横向偏移 |
| `verticalOffset` | 2 | 高度偏移 |
| `useRandomOffset` | true | 随机偏移 |
| `escortSpeedMultiplier` | 1 | 速度倍率 |
| `mirrorMovement` | false | 镜像移动 |
| `spawnOnStart` | true | 开始时生成 |
| `continuousSpawning` | true | 是否持续生成 |
| `destroyWithParent` | true | 是否随父级销毁 |
| `escortScaleRange` | `0.5~1.5` | 伴飞缩放范围 |
| `useHeightMask` | true | 使用高度遮罩 |

## 专用/遗留组件速查

这些接口仍可用，但部分是旧式实现或专用功能。新内容优先使用前面更现代的接口。

### CompDamageFactors

入口：

```xml
<li Class="SRA.CompProperties_DamageFactors">
  ...
</li>
```

作用：

- 在 `PostPreApplyDamage` 阶段把某些伤害完全免疫。
- 若伤害不在 `whitelist`，且 ranged/non-ranged 开关禁止，或伤害量大于等于 `damageCap`，则吸收伤害。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `whitelist` | 空 | 白名单 DamageDef，不被该规则免疫 |
| `damageCap` | 100 | 大于等于该伤害时免疫 |
| `candamagedbyRanged` | true | 是否可被远程伤害 |
| `candamagedbynotRanged` | true | 是否可被非远程伤害 |
| `popOutCoolDown` | 30 | 免疫文字间隔 |
| `popOutString` | `SRAimmune` | 弹出文字。旧字段，直接文本 |

### 动态建筑/武器渲染

入口：

```xml
<li Class="SRA.CompProperties_TurretRenderDynamic">...</li>
<li Class="SRA.CompProperties_WeaponRenderDynamic">...</li>
```

用途：

- 按帧播放建筑炮塔或武器贴图。
- 字段包括贴图路径、总帧数、每帧 tick、绘制尺寸、偏移、颜色、glow shader。

示例：

```xml
<li Class="SRA.CompProperties_TurretRenderDynamic">
  <texturePath>SRA/Turrets/TurretAnim</texturePath>
  <totalFrames>8</totalFrames>
  <ticksPerFrame>4</ticksPerFrame>
  <drawSize>(3, 3)</drawSize>
  <offset>(0, 0, 0)</offset>
  <useGlowShader>true</useGlowShader>
</li>
```

### Holographic

入口：

```xml
<li Class="SRA.CompProperties_Holographic">
  ...
</li>
```

用途：

- 建筑多图 hologram 展示。
- 支持浮动、透明度、转场、自动播放和手动切换。
- 部分 UI 字段是旧式 key 字符串，默认值为 `Holographic.*`。

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `graphics` | null | `GraphicData` 列表 |
| `floatAmplitude` | 0 | 浮动幅度 |
| `floatSpeed` | 0 | 浮动速度 |
| `opacity` | 1 | 透明度 |
| `transitionDuration` | 60 | 转场 tick |
| `autoplayIntervalTicks` | 600 | 自动播放间隔 |

### OpenCustomUI

入口：

```xml
<li Class="SRA.CompProperties_OpenCustomUI">
  <uiDefName>SRA_TestEvent</uiDefName>
  <label>SRA_OpenEvent</label>
  <failReason>SRA_CannotReach</failReason>
</li>
```

用途：

- 给建筑添加交互，打开指定 `EventDef`。
- 注意：`label` 和 `failReason` 是旧式文本字段，应传入可翻译 key 或未来迁移为 key 字段。

### GlobalMechCommand

入口：

```xml
<li Class="SRA.CompProperties_GlobalMechCommand" />
```

用途：

- 挂在 pawn ThingDef 上，使其拥有全局机械指挥范围。

### UseEffect_ActivateMech

入口：

```xml
<li Class="SRA.CompProperties_UseEffect_ActivateMech">
  <pawnKindDef>Mech_Lancer</pawnKindDef>
  <requireMechanitor>true</requireMechanitor>
</li>
```

用途：

- 物品使用效果，生成/激活指定机械单位。

### WeaponHediffGiver

入口：

```xml
<li Class="SRA.CompProperties_WeaponHediffGiver">
  <hediff>SRA_WeaponStateHediff</hediff>
</li>
```

用途：

- 武器装备时给 pawn 添加 hediff，卸下时移除。

### VehicleWeapon

入口：

```xml
<li Class="SRA.CompProperties_VehicleWeapon">
  ...
</li>
```

用途：

- 给 pawn 绘制随瞄准旋转的载具武器。
- 需要 `drawData` 配置偏移层级。

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `drawData` | null | 绘制偏移数据 |
| `turretRotationFollowPawn` | false | 炮塔是否跟随 pawn 朝向 |
| `horizontalFlip` | false | 水平翻转 |
| `rotationSmoothTime` | 0.12 | 旋转平滑时间 |
| `defaultWeapon` | null | 默认装备武器 |
| `drawSize` | 0 | 绘制尺寸，0 用武器贴图尺寸 |

### Excalibur Beam 能力

入口：

```xml
<li Class="SRA.CompProperties_AbilityExcaliburBeam">
  ...
</li>
```

字段：

| 字段 | 作用 |
| --- | --- |
| `beamDefName` | 生成的 beam ThingDef 名称 |
| `damageAmount` | 伤害 |
| `armorPenetration` | 穿甲 |
| `pathWidth` | 光束宽度 |
| `damageDef` | 伤害类型 |
| `soundDef` | 释放声音 |

### Tachyon Lances Verb

入口：

```xml
<verbClass>SRA.Verb_KT_Tachyon_Lances</verbClass>
```

需要 `verbProperties` 使用：

```xml
<li Class="SRA.VerbProperties_KT_Tachyon_Lances">
  <verbClass>SRA.Verb_KT_Tachyon_Lances</verbClass>
  ...
</li>
```

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `pathWidth` | 1 | 光束路径宽度 |
| `damageDef` | null | 伤害类型 |
| `damageAmount` | -1 | 伤害，负数使用武器 tool power |
| `armorPenetration` | -1 | 穿甲，负数使用武器 tool armorPenetration |
| `maxRange` | 1000 | 最大延伸距离 |
| `beamDefName` | `KT_Tachyon_LancesBeam` | beam ThingDef 名称 |

### Arc Verb

入口：

```xml
<li Class="SRA.VerbProperties_Arc">
  <verbClass>SRA.Verb_ShootArc</verbClass>
  ...
</li>
```

字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `damageDef` | null | 伤害类型 |
| `EMPDamageAmount` | -1 | EMP 伤害 |
| `damageAmount` | -1 | 伤害 |
| `armorPenetration` | -1 | 穿甲 |
| `affectedAngle` | 0 | 影响角度 |
| `isConductible` | false | 是否传导 |
| `conductNum` | 0 | 传导次数 |
| `conductFriendly` | false | 是否传导友军 |
| `conductHostile` | true | 是否传导敌军 |

### Pulse Electrode

入口：

```xml
<li Class="SRA.CompProperties_PulseElectrode">
  ...
</li>
```

用途：

- 建筑电弧炮塔。
- 支持炮塔旋转、强制目标、EMP/爆炸、器官伤害规则、电弧绘制、尸体处理。
- `label` 和 `description` 是旧式直接文本字段。

主要字段：

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `label` | `Pulse Electrode` | 按钮标题，旧式文本 |
| `description` | `Releases high-energy electrical arcs.` | 按钮说明，旧式文本 |
| `turretOffset` | `(0,0)` | 炮塔偏移 |
| `arcStartOffset` | `(0,0)` | 电弧起点偏移 |
| `turretTexPath` | 空 | 炮塔贴图 |
| `turretDrawSize` | 1 | 炮塔绘制尺寸 |
| `turnSpeed` | 10 | 转速 |
| `range` | 25 | 射程 |
| `minRange` | 0 | 最小射程 |
| `requireLineOfSight` | true | 是否需要视线 |
| `damageDef` | null | 伤害类型 |
| `damageAmount` | 35 | 伤害 |
| `armorPenetration` | -1 | 穿甲 |
| `empDamageAmount` | 20 | EMP 伤害 |
| `explosionRadius` | 1.9 | 爆炸半径 |
| `organDamages` | 空 | 器官伤害规则 |
| `cooldownTicks` | 120 | 冷却 |
| `lightningMatPath` | `Weather/LightningBolt` | 电弧材质 |

## 本地化键

SRALib 自带中英文 keyed：

- `SRA_RemoteMonitoring_*`
- `SRA_RemoteArtillery_*`
- `SRA_ClearTimedGameConditions_*`
- `SRA_BuildingDamageAdjuster*`
- `SRA_ResurrectionBeacon_*`
- `SRA_WarUnitSpawner_*`
- `SRA_Barrier*`
- `SRA_NeedMinTipExtra`
- `SRA_RegenTipExtra`
- `SRA_IgnoreTerrainCostTipExtra`
- `SRAExecuteDeath*`
- `SRAWeaponSwitcher*`

推荐写法：

```xml
<SRA_MyKey>这里是中文文本</SRA_MyKey>
```

然后在 Def 中填：

```xml
<buttonLabelKey>SRA_MyKey</buttonLabelKey>
```

不要在新 Def 中直接写：

```xml
<buttonLabel>这里是直接文本</buttonLabel>
```

例外：

- 少数旧接口仍保留 `label`、`description`、`popOutString` 这类字段。为了兼容它们可以继续使用，但新功能应优先使用 `xxxKey`。

## 推荐迁移规则

- 新建筑交互按钮统一使用 keyed 字段。
- 子 mod Def 不要依赖 SRALib 内不存在的占位 Def。
- 多单位战争单元生成器必须明确配置 `units`。
- 爆炸需要器官/全身 hediff 后处理时使用 `preNotifyEffects` 或 `postNotifyEffects`，不要在 `DamageWorker` 中绕过 `ApplyDamage`。
- 需要炮管制退、炮口火焰或多炮管射击时，统一让 projectile 出口、炮管中心和火焰中心都基于 `ModExtension_ShootWithOffset.offsets`。
- 炮塔需要转速限制时使用 `Building_TurretGunHasSpeed`。
- 需要持续 burst 转火时，把 `CompProperties_SustainedShoot` 挂在武器上，并使用 `Verb_ShootWithOffset`。
