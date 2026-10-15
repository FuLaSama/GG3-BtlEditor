"""Independent consumer: pip install flatbuffers==25.12.19; does not call C# decoder."""
import json
import math
import struct
import sys
from pathlib import Path
from flatbuffers import flexbuffers as flex

def official(ref):
    if ref.IsMap:
        m=ref.AsMap
        keys=[k.AsKey for k in m.Keys]
        assert keys==sorted(keys,key=lambda k:k.encode('utf-8')) and len(set(keys))==len(keys)
        # Also exercise binary key lookup, not just positional enumeration.
        return {key:official(m[key]) for key in keys}
    if ref.IsTypedVector: return [official(v) for v in ref.AsTypedVector]
    if ref.IsFixedTypedVector: return [official(v) for v in ref.AsFixedTypedVector]
    if ref.IsVector: return [official(v) for v in ref.AsVector]
    if ref.IsString: return bytes(ref.AsStringBytes).decode('gb18030')
    if ref.IsKey: return ref.AsKey
    return ref.Value

def payload(b):
    u=lambda pos:struct.unpack_from('<I',b,pos)[0]
    table=u(0); vt=table-struct.unpack_from('<i',b,table)[0]
    slot=table+struct.unpack_from('<H',b,vt+24)[0]
    vector=slot+u(slot)
    return b[vector+4:vector+4+u(vector)]

folder=Path(sys.argv[1] if len(sys.argv)>1 else 'artifacts/flex-interop')
def same(a,b):
    if isinstance(a,dict) and isinstance(b,dict): return a.keys()==b.keys() and all(same(a[k],b[k]) for k in a)
    if isinstance(a,list) and isinstance(b,list): return len(a)==len(b) and all(same(x,y) for x,y in zip(a,b))
    if isinstance(a,bool) or isinstance(b,bool): return type(a) is type(b) and a==b
    return a==b

manifest=json.loads((folder/'manifest.json').read_text('utf-8'))
for entry in manifest:
    actual=official(flex.GetRoot((folder/(entry['name']+'.flex')).read_bytes()))
    expected=entry['expected']
    if entry['original_btl']:
        expected=official(flex.GetRoot(payload(Path(entry['original_btl']).read_bytes())))
        if entry['changed']:
            expected[0]['name']+='（核验）'
            expected[0]['audit_extra']=[2**64-1,-1,70000,1.25,True,None]
    assert same(actual,expected),entry['name']
    if entry['name']=='large-offset-and-scalar':
        assert math.copysign(1,actual[0]['negative_zero'])==-1
print(f'PASS: {len(manifest)} libraries read independently; sorted-key lookup, legacy Chinese, numeric values and modified sample libraries match.')
