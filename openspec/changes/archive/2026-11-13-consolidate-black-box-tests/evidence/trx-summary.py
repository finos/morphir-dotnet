import sys, re, collections
import xml.etree.ElementTree as ET
ns={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
root=ET.parse(sys.argv[1]).getroot()
defs={}
for ut in root.iterfind('.//t:UnitTest',ns):
    tm=ut.find('t:TestMethod',ns)
    defs[ut.get('id')]=tm.get('className')
cls=collections.defaultdict(lambda:[0,0,0,0.0])
tot=collections.Counter()
for r in root.iterfind('.//t:UnitTestResult',ns):
    c=defs.get(r.get('testId'),'?').split(',')[0]
    o=r.get('outcome')
    d=r.get('duration') or '00:00:00'
    h,m,s=d.split(':'); sec=int(h)*3600+int(m)*60+float(s)
    i={'Passed':0,'Failed':1}.get(o,2)
    cls[c][i]+=1; cls[c][3]+=sec; tot[o]+=1
print('TOTAL',dict(tot), 'summed %.0f s'%sum(v[3] for v in cls.values()))
for c,v in sorted(cls.items(), key=lambda kv:-kv[1][3]):
    print('%s|%d|%d|%d|%.1f'%(c,v[0],v[1],v[2],v[3]))
