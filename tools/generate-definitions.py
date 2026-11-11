#!/usr/bin/env python3
"""Writes src/Anarres.Core/Definitions/wotlk-3.3.5a.json and spell-enums-3.3.5a.json from an AzerothCore checkout.

The .dbc layouts come from the acore_world *_dbc table dumps (data/sql/base/db_world), checked against real .dbc
files; the spell names come from the enums in AzerothCore's headers.

Usage: generate-definitions.py <azerothcore-wotlk folder> <clean dbc folder> <Definitions output folder>
"""
import json
import os
import re
import struct
import sys

AC = sys.argv[1]
DBC_FOLDER = sys.argv[2]
OUT = sys.argv[3]

# The .dbc layouts
ac=AC; dbcdir=DBC_FOLDER
cpp=open(ac+'/src/server/game/DataStores/DBCStores.cpp').read()
loads=re.findall(r'LOAD_(GT_)?DBC\(\s*(\w+)\s*,\s*"([\w.]+)"\s*,\s*"(\w+)"',cpp)
TYPES={'int':'Int32','int unsigned':'UInt32','bigint unsigned':'UInt32','bigint':'Int32','float':'Float',
 'tinyint unsigned':'Byte','tinyint':'SByte','smallint':'Int16','smallint unsigned':'UInt16','text':'String','tinytext':'String','mediumtext':'String','longtext':'String'}
SIZE={'Int32':4,'UInt32':4,'Float':4,'String':4,'Byte':1,'SByte':1,'Int16':2,'UInt16':2}
def cols(table):
    p=f'{ac}/data/sql/base/db_world/{table}.sql'
    if not os.path.exists(p): return None
    s=open(p).read(); body=s[s.find('CREATE TABLE'):]; body=body[:body.find(') ENGINE')]
    out=[]
    for line in body.split('\n')[1:]:
        m=re.match(r'\s*`(\w+)`\s+([a-z]+)(\(\d+\))?( unsigned)?',line)
        if m:
            t=m.group(2)+(m.group(4) or '')
            if m.group(2) in ('varchar','char'): t='text'
            out.append((m.group(1),TYPES[t]))
    return out
defs=[]
for gt,store,file,table in loads:
    c=cols(table)
    if c is None: print('no sql',file,file=sys.stderr); continue
    p=os.path.join(dbcdir,file)
    h=struct.unpack('<4s4I',open(p,'rb').read(20))
    rows,nf,rs=h[1],h[2],h[3]
    rowIndex=False
    if len(c)==nf+1 and c[0][0].upper()=='ID':
        rowIndex=True; c=c[1:]
    size=sum(SIZE[t] for _,t in c)
    if len(c)==nf and size!=rs and rs==4*nf:
        c=[(n,{'Byte':'UInt32','SByte':'Int32','Int16':'Int32','UInt16':'UInt32'}.get(t,t)) for n,t in c]; size=rs
    if len(c)!=nf or size!=rs:
        print('BAD',file,len(c),nf,size,rs,file=sys.stderr); continue
    defs.append({'File':file,'Table':table,'IndexIsRowNumber':rowIndex,'Columns':[{'Name':n,'Type':t} for n,t in c]})
print(len(defs),file=sys.stderr)
json.dump({'Build':'3.3.5a (12340)','Source':'Generated from AzerothCore data/sql/base/db_world/*_dbc.sql by tools/generate-definitions.py','Definitions':defs},open(os.path.join(OUT,'wotlk-3.3.5a.json'),'w',newline='\n'),indent=2)

# The spell names
src=os.path.join(AC,'src','server')
files=['shared/SharedDefines.h','game/Spells/Auras/SpellAuraDefines.h','game/Spells/SpellDefines.h','game/Spells/SpellMgr.h','game/Entities/Unit/UnitDefines.h','game/Spells/SpellInfo.h']
# (key, C++ enum name, prefix to strip, is flags)
WANT=[('Effect','SpellEffects','SPELL_EFFECT_',False),('Aura','AuraType','SPELL_AURA_',False),('Target','Targets','TARGET_',False),
('Attributes','SpellAttr0','SPELL_ATTR0_',True),('AttributesEx','SpellAttr1','SPELL_ATTR1_',True),('AttributesEx2','SpellAttr2','SPELL_ATTR2_',True),
('AttributesEx3','SpellAttr3','SPELL_ATTR3_',True),('AttributesEx4','SpellAttr4','SPELL_ATTR4_',True),('AttributesEx5','SpellAttr5','SPELL_ATTR5_',True),
('AttributesEx6','SpellAttr6','SPELL_ATTR6_',True),('AttributesEx7','SpellAttr7','SPELL_ATTR7_',True),
('Mechanic','Mechanics','MECHANIC_',False),('Dispel','DispelType','DISPEL_',False),('Power','Powers','POWER_',False),
('School','SpellSchoolMask','SPELL_SCHOOL_MASK_',True),('AuraState','AuraStateType','AURA_STATE_',False),
('DamageClass','SpellDmgClass','SPELL_DAMAGE_CLASS_',False),('PreventionType','SpellPreventionType','SPELL_PREVENTION_TYPE_',False),
('SpellFamily','SpellFamilyNames','SPELLFAMILY_',False),('CastTargetFlags','SpellCastTargetFlags','TARGET_FLAG_',True),
('InterruptFlags','SpellInterruptFlags','SPELL_INTERRUPT_FLAG_',True),('AuraInterruptFlags','SpellAuraInterruptFlags','AURA_INTERRUPT_FLAG_',True),
('ChannelInterruptFlags','SpellChannelInterruptFlags','',True),('ProcFlags','ProcFlags','PROC_FLAG_',True),
('ShapeshiftForm','ShapeshiftForm','FORM_',False),('CreatureType','CreatureType','CREATURE_TYPE_',False)]
text={f:open(os.path.join(src,f)).read() for f in files}
def find(name):
    for f,t in text.items():
        m=re.search(r'^enum (?:class )?'+name+r'\b[^{;]*\{(.*?)^\};',t,re.S|re.M)
        if m: return m.group(1)
def nice(s,prefix):
    if prefix and s.startswith(prefix): s=s[len(prefix):]
    return ' '.join(w.capitalize() if not w.isdigit() else w for w in s.split('_')) if s else s
out={}
for key,name,prefix,flags in WANT:
    body=find(name)
    if body is None: print('missing',name,file=sys.stderr); continue
    vals={}; items=[]; nxt=0
    for line in body.split('\n'):
        m=re.match(r'\s*([A-Z][A-Z0-9_]*)\s*(?:=\s*([^,/]+?))?\s*,?\s*(?://\s*(.*))?$',line)
        if not m: continue
        n,expr,comment=m.group(1),m.group(2),(m.group(3) or '').strip()
        if expr is None: v=nxt
        else:
            e=expr.strip()
            for k in sorted(vals,key=len,reverse=True): e=re.sub(r'\b'+k+r'\b',str(vals[k]),e)
            e=re.sub(r'(0x[0-9A-Fa-f]+|\d+)[uUlL]+',r'\1',e)
            try: v=int(eval(e,{'__builtins__':{}}))
            except Exception: print('skip',name,n,expr,file=sys.stderr); continue
        vals[n]=v; nxt=v+1
        if re.search(r'(TOTAL|MAX|COUNT|_ALL|MASK_ALL|_END)$',n) or n.endswith('_NONE') and flags: continue
        if flags and (v==0 or v & (v-1)): continue
        title=''; desc=''
        tm=re.search(r'TITLE\s+(.*?)(?:\s+DESCRIPTION\s+(.*))?$',comment)
        if tm: title=tm.group(1).strip(); desc=(tm.group(2) or '').strip()
        else: desc=re.sub(r'^\d+\s+','',comment)
        label=re.sub(r'@Attr\d+$','',title or nice(n,prefix)).strip()
        label=re.sub(r'^\d+\s+(?=[A-Za-z])','',label)
        items.append({'Value':v,'Name':n,'Label':label,'Description':desc})
    out[key]={'Flags':flags,'Values':items}
    print(key,len(items),file=sys.stderr)
out['School']={'Flags':True,'Values':[{'Value':1<<i,'Name':'SPELL_SCHOOL_MASK_'+n.upper(),'Label':n,'Description':''}
    for i,n in enumerate(['Physical','Holy','Fire','Nature','Frost','Shadow','Arcane'])]}
with open(os.path.join(OUT,'spell-enums-3.3.5a.json'),'w',newline='\n') as stream:
    json.dump({'Source':'Generated from AzerothCore src/server headers by tools/generate-definitions.py','Enums':out},
              stream,indent=2)
    stream.write('\n')
