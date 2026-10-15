"""Build tracked labels/schema overlay from the research FBS. Run from repository root.
python tools/BuildResearchLayout/generate.py <annotated.fbs>
The input is read only; UI names below are deliberately concise.
"""
import hashlib,json,re,sys
from pathlib import Path

labels={
'Size':('地图尺寸','总宽 总高 左侧留白 顶部留白 可玩宽度 可玩高度'),
'TileAttr':('地形贴图','地形 变种 横向偏移 纵向偏移'),
'Inline3':('三字节状态','着火标记 轮转槽 剩余计数'),
'TriggerAction':('开放矩形','最小横坐标 最小纵坐标 最大横坐标 最大纵坐标'),
'AgentInfo':('部队信息','地图格 势力 单位UID 兵种 经验 编数 朝向 移动力 当前生命 最大生命 血率 升级进度'),
'FactionMetadata':('势力信息','战役ID 国家 阵营 玩家控制 战败条件 占位1 金币 科技 金钱收入 科技收入 部队血率 势力颜色 占位2 关联地图格'),
'MapTerrain':('地形','地图尺寸 默认地质 地块属性 地形贴图 战争迷雾'),
'StageTarget':('关卡目标','目标类型 目标值 回合 目标ID 完成情况'),
'StageMetadata':('关卡','关卡目标 总回合'),
'ReinforcePoint':('增援点','地图格 朝向 仅限海军 兵种限制'),
'BattleInfo':('增援点','占位参数 增援点列表'),
'FactionForceEntry':('特种部队','部队ID 等级 招募冷却'),
'FactionAssignEntry':('卡牌状态','卡牌ID 剩余冷却'),
'FactionTargetEntry':('AI倾向','目标势力 AI倾向值'),
'FactionArmyLimitEntry':('击败统计','目标标记 累计击败'),
'FactionTechBindEntry':('势力加成','加成ID 剩余计数 轮转槽'),
'FactionStageNum':('国家行为树','行为树编号 运行状态'),
'FactionDetailConfig':('势力动态','将领池 卡牌使用数 策略覆盖 已撤离数 累计击杀 击败分类 消耗状态 选择关系 未知8 未知9 外交待办 未知11 判败已通知 回合进行中 已判败 战略出局 势力加成 未知17 未知18 受限招募数 科技进度 将领关联索引 状态参数 征服计时A 征服计时B'),
'Faction':('势力','基础信息 兵种科技 特种部队 卡牌状态 动态状态 AI倾向 国家行为树 集团军科技 国家策略 战略AI'),
'FactionInfo':('势力','势力列表'),
'BuildingExtraData':('占领奖励','奖励类型 奖励数量'),
'BuildingData':('建筑信息','所属势力 建筑ID 雪地贴图 贴图变体 横向偏移 纵向偏移'),
'TriggerEventSub2':('招募部队AI','AI类型 积极程度 目标地图格 移动限制 警戒半径 目标单位UID 警戒地图格'),
'TriggerEventSub3':('建筑加成','加成ID 剩余计数 轮转槽'),
'TriggerEventSub4':('增援落点','未知0 评分修正 未知2'),
'TriggerEventSub5':('建筑行为树','行为树编号 运行状态'),
'TriggerEventBldg':('建筑','基础信息 占领奖励 招募部队AI 建筑加成 增援落点 建筑行为树 招募朝向 集团军设施'),
'TriggerEventFort':('工事','工事ID 建造轮转槽 剩余建造回合 贴图朝向'),
'TriggerEvent':('地块事件','地图格 城市名称 目标标记 建筑 工事 着火状态 占领奖励'),
'TriggerNode':('省份','锚点地图格 所属势力 是否有效 间谍标记 情报误导'),
'TriggerInfo':('地标与省份','地块事件 撤离动画 省份划分 图标坐标 开放矩形 启用省份 省份数据 开放序号'),
'AIAgentBehavior':('部队AI','AI类型 积极程度 目标地图格 移动限制 警戒半径 目标单位UID 警戒地图格'),
'Table8Entry':('部队加成','加成ID 剩余计数 轮转槽'),
'AIAgentTable8':('部队动态','移动计数 升级次数 临时加成 再动次数 航母补充 回合临时值 状态位 记忆地图格'),
'AIAgentTable10':('特种配置','特种部队 等级 当前生命 最大生命 血率 升级标记'),
'AIAgentTable11':('将领配置','将领ID 激活状态 晋升等级 勋章ID'),
'AIAgentTable12':('部队行为树','行为树编号 运行状态'),
'AIAgent':('部队','基础信息 国家兵种加成 关键目标 部队AI 未知4 初始士气 士气恢复 结算状态 动态状态 未用状态 特种配置 将领配置 部队行为树 未知13 击败奖励'),
'AIBehavior':('部队组','组ID 成员UID'),
'AIRoute':('战略驻军组','容器ID 成员UID'),
'AIInfo':('部队','单位列表 部队组 下个单位UID 战略驻军组'),
'SubRegion':('Lua触发组','触发组ID 回调函数 仍可触发'),
'RegionInfo':('Lua触发状态','脚本路径 触发组 事件数据 待执行动作'),
'CardEntry':('指定格增援','目标地图格 单位UID 执行回合'),
'FactionCardsConfig':('按格增援','投放地图格 待执行增援'),
'LimitEntry':('自动增援','目标地图格 单位UID 执行回合'),
'FactionLimitsConfig':('按势力增援','势力 待执行增援'),
'TechUpgradeEntry':('待用卡牌','卡牌ID 保留值 使用回合'),
'TechUpgradeConfig':('卡牌队列','势力 待用卡牌'),
'GeneralDiplomacyEntry':('可购买增援','增援配置ID 剩余次数 已支付'),
'CityStatus':('空中支援','支援类型 发起机场 势力 执行回合'),
'FactionCityStatus':('按机场支援','发起机场 待执行支援'),
'HarborStatus':('自动支援','支援类型 发起机场 势力 执行回合'),
'FactionHarborStatus':('按势力支援','势力 待执行支援'),
'StageConfig':('增援与军需','按格增援 按势力增援 卡牌队列 可购买增援 剩余军需 按机场支援 按势力支援 已用折扣奖章'),
'Decal':('天气','天气类型 开始回合 持续回合'),
'DecalInfo':('天气','天气列表'),
'ViewBoundary':('玩家将领池','将领列表'),
'Root':('关卡','结构版本 地形 关卡 增援点 势力 地标与省份 部队 Lua触发状态 增援与军需 天气 共用行为树 玩家将领池'),
}

source=Path(sys.argv[1]).read_text('utf-8-sig')
types={}; declarations=[]
for kind,name,body in re.findall(r'\b(table|struct)\s+(\w+)\s*\{(.*?)\}',source,re.S):
    if name.startswith('_'):continue
    title,names=labels[name];names=names.split(); fields=[]; entries={}
    for line in body.splitlines():
        match=re.match(r'\s*(\w+)\s*:\s*([^;]+);\s*(?://\s*(.*))?',line)
        if not match:continue
        field,definition,note=match[1],match[2],match[3] or ''
        label=names[len(fields)]; assert len(label)<=12,label
        tip=re.sub(r'\s*→.*','',note)
        tip=re.sub(r'\b[0-9A-F]{6}\b','',tip).replace('@alias(flag)','').strip()
        entries[field]={'label':label,'tip':tip,'uncertain':note.startswith('*') or '未确认' in note or '未知' in note}
        fields.append(f'  {field}:{definition};')
    assert len(fields)==len(names),(name,len(fields),len(names))
    types[name]={'label':title,'fields':entries}
    if name=='AgentInfo':
        # Existing object API uses two packed ushort members for four bytes.
        # Byte layout is identical to the annotated FBS; keep member indexes stable.
        declarations.append('struct AgentInfo { cell_idx:short; faction_id:ushort; agent_id:ushort; unit_id:ushort; stack_count:ushort; val5:ushort; hp:ushort; max_hp:ushort; val8:ushort; val9:ushort; }')
    else:declarations.append(f'{kind} {name} {{\n'+ '\n'.join(fields)+'\n}')
types['AgentInfo']['fields']['stack_count']['label']='经验与编数'
types['AgentInfo']['fields']['val5']['label']='朝向与移动力'
types['AgentInfo']['fields']['stack_count']['tip']='16 位合并值：低 8 位为经验，高 8 位为编数。日常编辑可用基础栏的分项输入。'
types['AgentInfo']['fields']['val5']['tip']='16 位合并值：低 8 位为朝向，高 8 位为移动力。日常编辑可用基础栏的分项输入。'
result={'version':1,'revision':'research.2026-10-15','source_sha256':hashlib.sha256(source.encode()).hexdigest(),'types':types}
Path('EditorLayout/fields/research-labels.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n','utf-8')
Path('EditorLayout/scripts/common/研究结构.lua').write_text('-- 按人工命名版研究修订物理类型；AgentInfo 保留等宽位段表示，兼容既有对象成员下标。\neditor.format.schema {\n  id="builtin.research.20261015",\n  definition=[[\n'+ '\n\n'.join(declarations)+'\n]],\n}\n','utf-8')
print('Generated',len(types),'types,',sum(len(t['fields']) for t in types.values()),'field labels.')
