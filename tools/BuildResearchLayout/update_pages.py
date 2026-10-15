"""One-time migration of existing short labels and XML details sections. Idempotent."""
from pathlib import Path
import xml.etree.ElementTree as E

def save(path,root):
    E.indent(root,space='  ')
    path.write_text('<?xml version="1.0" encoding="utf-8"?>\n'+E.tostring(root,encoding='unicode')+'\n','utf-8')
def details(root,id,title,bind,model,omit='',**extra):
    if root.find(f"section[@id='{id}']") is None:
        E.SubElement(root,'section',dict(id=id,title=title,kind='details',bind=bind,model=model,omit=omit,labels='../fields/research-labels.json',collapsible='true',collapsed='true',**extra))
for filename in ['unit','site','faction','stage','terrain','country']:
    path=Path(f'EditorLayout/pages/{filename}.xml'); root=E.fromstring(path.read_text('utf-8-sig'))
    for i,g in enumerate(root.iter('group')):
        g.set('id',g.get('id',filename+'_group_'+str(i)))
        g.set('collapsible','true');g.set('collapsed','false' if i==0 or g.get('id')=='offmap_0' else 'true')
    for s in root.findall('section'):
        if s.get('kind')=='details':s.set('collapsible','true')
        if s.get('kind')=='list':s.set('collapsible','true');s.set('collapsed','false')
    if filename=='unit':
        mapping={('unit',):'兵种ID',('faction','agent'):'所属势力 / 单位UID',('hp','max_hp'):'当前 / 最大生命',
          ('stack','level'):'编数 / 经验',('play_mode','ai_target'):'关键目标 / 初始士气',('faction_extra','mobility'):'士气恢复 / 移动力',
          ('direction','val8'):'朝向 / 血率',('val9',):'升级进度',('behavior_0',):'AI类型',('behavior_id',):'积极程度',
          ('behavior_2',):'目标地图格',('behavior_radius',):'移动限制',('behavior_4',):'警戒半径',('behavior_center',):'目标单位UID',
          ('behavior_6',):'警戒地图格',('general_id',):'将领ID',('general_param2',):'晋升等级',('ex_id',):'特种部队ID',
          ('ex_hp','ex_max_hp'):'当前 / 最大生命',('ex_1','ex_4'):'等级 / 血率',('ex_5',):'升级标记'}
        for row in root.iter('row'):
            ids=tuple(f.get('id') for f in row.findall('field'))
            if ids in mapping:row.set('label',mapping[ids])
        for f in root.iter('field'):
            if f.get('id')=='general_active':f.set('label','激活状态 *')
        group=next(g for g in root.iter('group') if g.get('title')=='将领配置')
        if group.find("row/field[@id='general_medal']") is None:
            row=E.SubElement(group,'row',label='勋章ID')
            E.SubElement(row,'field',id='general_medal',field='unit.general.medal',when='general')
        medal=group.find("row/field[@id='general_medal']")
        medal.attrib.pop('bind',None);medal.attrib.pop('type',None);medal.set('field','unit.general.medal')
        details(root,'unit_details','部队详细配置','/6/0','AIAgent','0,2,3,5,6,10,11',resolve='unit')
        root.find("section[@id='unit_details']").set('selection','/6/0')
        root.find("section[@id='unit_details']").set('omit','2,3,5,6,10,11')
        outside=root.find("section[@filter='offmap']")
        old=outside.find('fields')
        if old is not None:
            # 同一套配置在地图外列表中也分组，避免再次铺开全部字段。
            templates=list(root.find('section').findall('group'))
            for i,template in enumerate(templates):
                group=E.fromstring(E.tostring(template,encoding='unicode'))
                group.set('id','offmap_'+str(i))
                outside.insert(1+i,group)
            outside.remove(old)
        details(root,'unit_groups','部队组与驻军','/6','AIInfo','0,1')
    if filename=='site':
        names={('flag',):'所属势力',('type',):'建筑ID',('extra',):'雪地贴图 *',('owner',):'贴图变体',('dx','dy'):'横向 / 纵向偏移',
               ('field6',):'招募朝向 *',('fort_id',):'工事ID',('field1',):'城市名称ID',('field3',):'贴图朝向'}
        for row in root.iter('row'):
            ids=tuple(f.get('id') for f in row.findall('field'))
            if ids in names:row.set('label',names[ids])
        details(root,'event_details','地块与地标详情','/5/0','TriggerEvent','0',resolve='event')
        details(root,'province_details','省份与开放区域','/5','TriggerInfo','0')
        root.find("section[@id='province_details']").set('omit','')
        root.find("section[@id='province_details']").set('title','全部地标与省份')
    if filename=='faction':
        fields={'id':'战役ID','country':'国家ID','camp':'阵营','is_ai':'玩家控制','val5':'战败条件','align1':'占位1','income':'金钱收入倍率',
                'damage':'科技收入倍率','hp':'部队血率','align2':'占位2','config_id':'关联地图格','general_flag':'集团军科技','config_ref':'国家策略'}
        for f in root.iter('field'):
            if f.get('id') in fields:f.set('label',fields[f.get('id')])
            if f.get('id')=='config_id':f.set('type','i16')
        root.find(".//field[@id='is_ai']").set('tip','0 为电脑，非零为玩家控制。')
        route=root.find("section[@bind='/6/1']");route.set('title','部队组')
        for c,label in zip(route.findall('columns/column'),['组ID','成员UID']):c.set('header',label)
        route.find("fields/field[@id='flag']").set('label','组ID')
        route.find("fields/field[@id='cells']").set('label','成员UID')
        route.find("actions/action[@id='add']").set('label','添加部队组')
        route.find("actions/action[@id='update']").set('label','保存部队组')
        route.find("actions/action[@id='delete']").set('label','删除部队组')
        route.find("actions/action[@id='delete']").set('confirm','确定删除选中的部队组吗？')
        details(root,'faction_details','势力详细配置','/4/0','Faction','0,7,8',selection='/4/0')
    if filename=='stage':
        target=root.find("section[@bind='/2/0']")
        for c,label in zip(target.findall('columns/column'),['类型','目标值','回合','目标ID','完成情况']):c.set('header',label)
        for f,label in zip(target.findall('fields/field'),['目标类型','目标值','回合','目标ID','完成情况']):f.set('label',label)
        point=root.find("section[@bind='/3/1']");point.set('title','增援点')
        for c,label in zip(point.findall('columns/column'),['地图格','朝向','仅限海军','兵种限制']):c.set('header',label)
        for f,label in zip(point.findall('fields/field'),['地图格','朝向','仅限海军','兵种限制']):f.set('label',label)
        point.find("fields/field[@id='cell']").set('type','i16')
        point.find("actions/action[@id='add']").set('label','添加增援点')
        point.find("actions/action[@id='delete']").set('label','删除增援点')
        point.find("actions/action[@id='delete']").set('confirm','确定删除选中的增援点吗？')
        details(root,'stage_details','脚本、增援与存档','/','Root','1,2,3,4,5,6,9,10')
        details(root,'reinforce_details','增援点附加项','/3','BattleInfo','1')
    if filename=='terrain':details(root,'terrain_details','地形详细配置','/1','MapTerrain','0,2,3,4')
    save(path,root)
