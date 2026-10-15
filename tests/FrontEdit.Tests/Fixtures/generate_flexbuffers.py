"""Regenerate independent fixtures: pip install flatbuffers==25.12.19.
No game files or production encoder are used. JSON String bytes follow GG3 GB18030.
"""
import base64
import json
from pathlib import Path
import flatbuffers
from flatbuffers import flexbuffers as flex


class GameText(str):
    def encode(self, encoding='utf-8', errors='strict'):
        return super().encode('gb18030', errors)


def official(ref):
    if ref.IsMap:
        return {k.AsKey: official(v) for k, v in zip(ref.AsMap.Keys, ref.AsMap.Values)}
    if ref.IsTypedVector:
        return [official(v) for v in ref.AsTypedVector]
    if ref.IsFixedTypedVector:
        return [official(v) for v in ref.AsFixedTypedVector]
    if ref.IsVector:
        return [official(v) for v in ref.AsVector]
    if ref.IsString:
        return bytes(ref.AsStringBytes).decode('gb18030')
    if ref.IsKey:
        return ref.AsKey
    return ref.Value


fixtures = []
def add(name, raw, expected=None, supported=True):
    if supported:
        assert official(flex.GetRoot(raw)) == expected, name
    fixtures.append(dict(name=name, bytes=base64.b64encode(raw).decode(), supported=supported, expected=expected))


add('empty', bytes(flex.Dumps([])), [])
for value in [-1, -129, -32769, -(2**40), -(2**63), 127, 128, 32767, 32768, 70000, 2**63-1]:
    obj=[{'btid':1, 'value':value}]
    add('signed-'+str(value), bytes(flex.Dumps(obj)), obj)
obj=[{'btid':1,'name':GameText('中文𠀀'),'node':[{'id':-1,'class':'wait','rounds':2}],
      'null':None,'bool':True,'float':1.25,'params':[]}]
add('nested-gb18030', bytes(flex.Dumps(obj)), obj)
for method, value in [('UInt', 2**64-1), ('IndirectInt', -(2**40)), ('IndirectUInt', 2**64-1),
                      ('IndirectFloat', 1.25), ('TypedVectorFromElements', [-1,2,300]),
                      ('TypedVectorFromElements', [True,False]), ('TypedVectorFromElements', [1.25,-2.0]),
                      ('FixedTypedVectorFromElements', [-1,2,300])]:
    builder=flex.Builder()
    with builder.Vector():
        with builder.Map():
            builder.Int('btid',1)
            builder.Key('value')
            getattr(builder,method)(value)
    add(method+'-'+str(len(fixtures)),bytes(builder.Finish()),[{'btid':1,'value':value}])
builder=flex.Builder()
with builder.Vector():
    with builder.Map():
        builder.Blob('blob',b'\x00\x01')
add('unsupported-blob',bytes(builder.Finish()),supported=False)
raw=bytearray(flex.Dumps([{'text':'abc'}]))
raw[raw.index(b'abc')-1]=255
add('bad-string-length',bytes(raw),supported=False)
raw=bytearray(flex.Dumps([{'btid':1}]))
raw[-1]=3
add('invalid-root-width',bytes(raw),supported=False)

target=Path(__file__).with_name('flexbuffers.json')
target.write_text(json.dumps({'generator':'flatbuffers '+flatbuffers.__version__, 'fixtures':fixtures},ensure_ascii=False,indent=2)+'\n','utf-8')
print(f'Wrote {len(fixtures)} official fixtures: {target}')
