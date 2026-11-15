"""Render and view-model fixtures of the own Battlegrounds hero statistics (docs/analytics-api.md, response C5).

Hand-made numbers on real hero ids, shaped as the pool publishes them: shares with a Wilson interval (z = 1.96),
mean place +- 1.96*s/sqrt(n), values hidden under 10 games, rows hidden entirely for too few installations.
Run from the repository root: python tools/analytics/render_fixture.py
Writes tests/Hearthpulse.Tests/Fixtures/Analytics/bg-heroes-render.json and bg-heroes-render-small.json.
"""
import json, math, random
random.seed(7)
def wilson(k,n,z=1.96):
    p=k/n; d=1+z*z/n; c=(p+z*z/(2*n))/d; h=z*math.sqrt(p*(1-p)/n+z*z/(4*n*n))/d
    return round(max(0,c-h),3), round(min(1,c+h),3)
def rate(k,n,show=True):
    if not show or n<10: return {"num":k,"den":n,"value":None,"low":None,"high":None}
    lo,hi=wilson(k,n); return {"num":k,"den":n,"value":round(k/n,3),"low":lo,"high":hi}
def row(hero, offered, dist=None, picked=None, hidden=False):
    if hidden:
        return {"hero":hero,"offered":offered,"picked":None,"pickRate":None,"placement":None,"top4":None,"first":None,"quality":"small_sample"}
    n=sum(dist); picked=picked or n
    if n>=10:
        mean=sum((i+1)*c for i,c in enumerate(dist))/n
        sd=math.sqrt(sum(c*((i+1)-mean)**2 for i,c in enumerate(dist))/(n-1)); h=1.96*sd/math.sqrt(n)
        place={"n":n,"mean":round(mean,3),"low":round(mean-h,3),"high":round(mean+h,3),"dist":dist}
    else:
        place={"n":n,"mean":None,"low":None,"high":None,"dist":None}
    r={"hero":hero,"offered":offered,"picked":picked,"pickRate":rate(picked,offered),"placement":place,
       "top4":rate(sum(dist[:4]),n),"first":rate(dist[0],n)}
    r["quality"]="ok" if offered>=10 and n>=10 else "small_sample"
    return r
def doc(heroes, captures, sources, suppressed, stale=False):
    heroes=sorted(heroes,key=lambda r:(-r["offered"],r["hero"]))
    return {"v":2,"metric":"bg.hero.offer_pick_placement","rulesVersion":"bg-hero-1","mode":"solo","available":True,"reason":None,
            "published":1794639600000,"stale":stale,"source":"source_capture","period":{"fromDay":20741,"toDay":20771,"days":30},
            "groups":[{"build":231548,"captures":captures,"sources":sources,"suppressed":suppressed,"heroes":heroes},
                      {"build":230012,"captures":41,"sources":9,"suppressed":12,"heroes":[row("BG20_HERO_100",12,[1,1,2,1,2,1,1,1],picked=10)]}]}
ok=doc([
 row("BG20_HERO_100",1240,[52,48,45,44,40,38,36,33],picked=352),
 row("BG20_HERO_101",96,[5,6,4,5,3,4,2,3],picked=34),
 row("BG20_HERO_202",71,[2,3,4,3,4,3,3,2],picked=26),
 row("BG21_HERO_000",44,[1,2,1,3,2,2,1,1],picked=14),
 row("BG20_HERO_283",17,[1,0,2,1,1,1,0,1],picked=8),
 row("BG21_HERO_010",9,hidden=True),
],captures=2310,sources=38,suppressed=3)
small=doc([
 row("BG20_HERO_100",14,[1,1,0,2,1,1,0,1],picked=8),
 row("BG20_HERO_101",9,[1,0,1,1,0,1,1,0],picked=6),
 row("BG20_HERO_202",8,hidden=True),
 row("BG21_HERO_000",6,hidden=True),
],captures=31,sources=6,suppressed=19,stale=True)
for name,d in (("bg-heroes-render.json",ok),("bg-heroes-render-small.json",small)):
    open("tests/Hearthpulse.Tests/Fixtures/Analytics/"+name,"w",encoding="utf-8",newline="").write(json.dumps(d,indent=1)+"\n")
print([r["quality"] for r in ok["groups"][0]["heroes"]],[r["quality"] for r in small["groups"][0]["heroes"]])
