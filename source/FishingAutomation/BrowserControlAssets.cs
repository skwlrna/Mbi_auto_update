namespace FishingAutomation;

// Static, self-contained UI. No external scripts, storage, or remote assets.
internal static class BrowserControlAssets
{
    internal const string Html = """
<!doctype html><html lang="ko"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>MOBI Automation</title><link rel="stylesheet" href="/style.css"><script src="/app.js" defer></script></head>
<body><main><header><strong>MOBI</strong><span>Automation · 원격 제어</span></header>
<section><h2>PC 인증</h2><label for="key">PC 환경 설정에서 발급한 연결 키</label>
<input id="key" type="password" autocomplete="off" spellcheck="false" placeholder="연결 키 입력"><button id="connect">연결</button>
<p class="hint">키는 이 화면을 닫으면 사라집니다.</p></section>
<section class="hero"><h2>현재 작업</h2><pre id="status">연결 대기</pre><button class="danger" id="stop" disabled>전체 작업 중지</button><p id="stopReply" role="status"></p></section>
<section><h2>오늘 작업</h2>
<ul class="today-work"><li><a href="#altering">가공</a><span id="todayAltering">PC 연결 대기</span></li>
<li><a href="#abyssHistory">어비스</a><span id="todayAbyss">PC 연결 대기</span></li>
<li><a href="#gatheringHistory">채집</a><span id="todayGathering">PC 연결 대기</span></li></ul></section>
<section id="altering"><h2>자동가공</h2><label for="itemSearch">품목 검색</label><input id="itemSearch" maxlength="120" placeholder="예: 가죽" disabled>
<button id="catalog" disabled>가공 품목 불러오기</button><p id="catalogReply" role="status"></p>
<label for="name">가공 품목 선택</label><select id="name" disabled><option value="">품목을 선택하세요</option></select>
<label for="quantity">수량</label><input id="quantity" type="number" min="1" max="1000000" value="100" inputmode="numeric">
<label for="mode">수량 방식</label><select id="mode"><option value="total">총 수량까지 채우기</option><option value="additional">추가 생산</option></select>
<div class="buttons"><button id="order" disabled>작업 등록</button><button id="queue" disabled>대기열 확인</button>
<button id="start" disabled>가공 시작</button><button id="qstop" disabled>가공 정지</button></div>
<p id="startReply" role="status"></p><p id="alteringIssue" class="hint" role="status"></p>
<div id="previousBatch"><pre id="previousDetail">PC 연결 후 이전 배치를 확인할 수 있습니다.</pre><button id="verifyResume" disabled>현재 상태 검증</button><pre id="resumeReply" role="status"></pre><button id="startResume" disabled>이어하기 시작</button><button id="resetPrevious" class="danger" disabled>이전 배치 초기화</button><p id="resetReply" role="status"></p></div>
<button id="clearQueue" class="danger" disabled>가공 목록 전체 비우기 · #1부터</button>
<div id="orders">등록된 작업을 확인하세요.</div><p id="queueReply" role="status"></p></section>
<section><h2>어비스</h2><label for="dungeon">던전 선택</label><select id="dungeon"><option>허상의 정박지</option><option>광기의 동굴</option><option>흩어진 물길</option></select>
<p class="hint">기존 어비스 방식으로 무한 반복합니다. 종료하려면 전체 작업 중지를 누르세요.</p><button id="abyss" disabled>어비스 시작</button><p id="abyssReply" role="status"></p><details id="abyssHistory"><summary>최근 어비스 판별 기록</summary><pre id="abyssRounds">PC 연결 후 확인할 수 있습니다.</pre></details></section>
<section><details id="gatheringHistory"><summary>오늘 채집 상세</summary><pre id="gatheringDetails">PC 연결 후 확인할 수 있습니다.</pre></details></section>
<section aria-live="polite"><h2>명령 응답</h2><pre id="reply">—</pre></section></main></body></html>
""";
    internal const string Css = """
*{box-sizing:border-box}body{margin:0;background:#f5f6f8;color:#232738;font:16px system-ui,sans-serif}
main{max-width:560px;margin:auto;padding:20px 16px 36px}header{display:flex;align-items:baseline;gap:12px;margin:8px 0 22px}
header strong{font-size:27px}header span,.hint{color:#656b7b;font-size:13px}section{background:white;border:1px solid #e1e4ec;border-radius:20px;padding:18px;margin-bottom:14px}
h2{font-size:17px;margin:0 0 14px}label{display:block;font-size:14px;margin:10px 0 5px}input,select,button{width:100%;min-height:44px;border-radius:11px;font:inherit}
input,select{border:1px solid #d9deeb;background:#f8f9fc;color:inherit;padding:8px 11px}button{border:0;background:#4b5ab8;color:white;padding:10px;margin-top:10px;font-weight:600;cursor:pointer}
button:disabled{opacity:.43;cursor:default}.buttons{display:grid;grid-template-columns:1fr 1fr;gap:8px}.hero{background:#24283b;color:#f7f8ff;border-color:#24283b}
.today-work{list-style:none;margin:0;padding:0}.today-work li{display:grid;grid-template-columns:64px 1fr;gap:8px;padding:9px 0;border-bottom:1px solid #9198a566}
.today-work li:last-child{border:0}.today-work a{color:inherit;font-weight:650}.today-work span{overflow-wrap:anywhere}
.queue-row{padding:10px 0;border-bottom:1px solid #9198a566}
.queue-row button{width:auto;margin:6px 0 0}
.danger{background:#b52b40}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.6 system-ui,sans-serif;margin:10px 0}
@media(prefers-color-scheme:dark){body{background:#17171d;color:#eff1fa}section{background:#23252e;border-color:#3e4252}input,select{background:#303340;border-color:#4a4e60}.hint,header span{color:#aab3c8}}
""";
    internal const string Js = """
'use strict';
const el=id=>document.getElementById(id);
const controls=['stop','order','queue','start','qstop','abyss'];
let key='', busy=false, online=false, previousBatch=null;
let poll=null, queueSignature='', startWatch=null, stopWatch=null;
let queueRevision='', canClearQueue=false, orderCount=0;
let catalog=[], catalogLoading=false;
let verifiedResume=null;
function enabled(value){
  online=value;
  for(const id of controls)el(id).disabled=!value||busy;
  for(const button of el("orders").querySelectorAll("button"))button.disabled=!value||busy;
  el('resetPrevious').disabled=!value||busy||!previousBatch;
  el('verifyResume').disabled=!value||busy||!previousBatch;
  el('startResume').disabled=!value||busy||!verifiedResume||verifiedResume.batchId!==previousBatch?.id;
  el('clearQueue').disabled=!value||busy||!canClearQueue;
  el('catalog').disabled=!value||busy||catalogLoading;
  el('itemSearch').disabled=el('name').disabled=!value||busy||catalog.length===0;
  el('order').disabled=!value||busy||!el('name').value;
}
async function api(path,payload,signal){
  const options={cache:'no-store',signal,headers:{Authorization:'Bearer '+key}};
  if(payload){options.method='POST';options.headers['Content-Type']='application/json';options.body=JSON.stringify(payload);}
  const r=await fetch(path,options); const data=await r.json();
  if(!r.ok)throw Error(data.message||'요청 실패');
  return data.message;
}
function renderPreviousBatch(batch){
  previousBatch=null;
  if(!batch.id){el('previousDetail').textContent=batch.message;return;}
  previousBatch=batch;
  const plans=batch.plans.map(plan=>plan.name+' · 목표 '+plan.quantity.toLocaleString('ko-KR')+'개');
  el('previousDetail').textContent=['이전 배치',new Date(batch.startedAt).toLocaleString('ko-KR'),...plans].join('\n');
}
function renderQueue(orders,revision){
  const signature=JSON.stringify([revision,orders]);
  if(signature===queueSignature)return;
  queueSignature=signature;
  const rows=orders.map(order=>{
    const row=document.createElement('div');row.className='queue-row';
    const label=document.createElement('div');
    label.textContent='#'+order.number+' '+order.name+' · '+(order.total?'총 ':'추가 ')+order.quantity.toLocaleString('ko-KR')+'개 · '+order.state+(order.detail?' · '+order.detail:'');
    row.append(label);
    if(order.state==='시작 대기'){
      const button=document.createElement('button');button.type='button';button.textContent='취소';
      button.setAttribute('aria-label',order.name+' #'+order.number+' 취소');
      const feedback=document.createElement('div');feedback.setAttribute('role','status');
      button.onclick=()=>{
        const number=Number(order.number);
        if(Number.isSafeInteger(number)&&number>0)
          command('queue-cancel',{Number:number,QueueRevision:revision},feedback);
        else feedback.textContent='이 작업 번호를 처리할 수 없습니다.';
      };
      row.append(button,feedback);
    }
    return row;
  });
  el('orders').replaceChildren(...(rows.length?rows:['등록된 가공 요청이 없습니다.']));
}
function renderState(state){
  el('status').textContent=state.status;
  el('todayAltering').textContent=state.todayWork?.altering??'오늘 가공 기록 없음';
  el('todayAbyss').textContent=state.todayWork?.abyss??'오늘 기록된 판 없음';
  el('todayGathering').textContent=state.todayWork?.gathering??'오늘 채집 기록 없음';
  el('gatheringDetails').textContent=(state.todayWork?.gatheringDetails??[]).join('\n')||'오늘 채집 기록 없음';
  el('abyssRounds').textContent=(state.abyssRounds??[]).join('\n\n')||'기록이 없습니다.';
  el('alteringIssue').textContent=state.alteringIssue??'';
  renderPreviousBatch(state.previousBatch);
  queueRevision=state.queueRevision;
  canClearQueue=state.canClearQueue;
  orderCount=state.orders.length;
  renderQueue(state.orders,queueRevision);
  if(startWatch){
    const {operation,feedback,reply}=startWatch;
    const preparing=operation==='queue-start' ? state.phoneAltering && !state.altering : operation==='abyss-start' && state.abyssPreparing;
    const active=operation==='resume-start'?state.altering:operation==='queue-start'?state.phoneAltering && state.altering:state.abyss;
    if(preparing || active){
      startWatch.observed=true;
      const needsAttention=operation==='queue-start' && state.orders.some(order=>order.state==='확인 필요');
      const stage=preparing?(operation==='queue-start'?'품목·재고 확인 중':'어비스 시작 준비 중'):needsAttention?'가공 실행 중 · 확인 필요 항목 있음':'실행 중';
      feedback.textContent=stage+' · '+state.detail;
    }else{
      feedback.textContent=state.alteringIssue ?? (startWatch.observed ? '실행 종료 · '+state.detail : reply+' · '+state.detail);
      startWatch=null;
    }
  }
  if(stopWatch){
    const active=stopWatch.operation==='queue-stop'?state.phoneAltering:state.running;
    stopWatch.feedback.textContent=active?'정지 요청 중 · '+state.detail:'정지 확인 · '+state.detail;
    if(!active)stopWatch=null;
  }
}
async function status(){
  if(!key||busy||poll)return;
  const current=new AbortController();
  poll=current;
  try{
    const state=JSON.parse(await api('/api/mobile-state',null,current.signal));
    if(!current.signal.aborted){renderState(state);online=true;}
  }catch(e){
    if(!current.signal.aborted){el('status').textContent=e.message;online=false;}
  }finally{
    if(poll===current)poll=null;
    enabled(online);
  }
}
async function command(operation,fields={},feedback=el('reply')){
  if(!online||busy)return;
  poll?.abort();poll=null;
  busy=true;enabled(online);
  const stopping=operation==='stop'||operation==='queue-stop';
  const starting=operation==='queue-start'||operation==='abyss-start'||operation==='resume-start';
  if(starting){startWatch=null;stopWatch=null;}
  if(stopping||starting)feedback.textContent=stopping?'정지 요청 중':'시작 요청 중';
  try{
    if(!globalThis.isSecureContext||!crypto.randomUUID)throw Error('보안 연결(HTTPS 또는 PC 로컬 주소)이 필요합니다.');
    const request={Operation:operation,RequestId:crypto.randomUUID(),...fields};
    const reply=await api('/api/command',request);
    if(operation!=='resume-verify')feedback.textContent=el('reply').textContent=reply;
    if(operation==='queue-cancel'||operation==='queue-clear')el('queueReply').textContent=reply;
    if(stopping && reply.includes('정지 요청 접수')){
      startWatch=null;
      stopWatch={operation,feedback};
    }
    if(starting && reply.includes('시작 요청 접수'))
      startWatch={operation,feedback,reply,observed:false};
    return reply;
  }catch(e){
    feedback.textContent=el('reply').textContent=e.message+' · 중복 실행하지 말고 PC 상태를 확인하세요.';
  }finally{
    busy=false;enabled(online);
    void status();
  }
}
el('connect').onclick=()=>{
  if(busy||catalogLoading)return;
  poll?.abort();poll=null;
  key=el('key').value.trim();el('key').value='';
  catalog=[];el('itemSearch').value='';renderCatalog();
  online=false;queueSignature='';startWatch=null;stopWatch=null;verifiedResume=null;el('resumeReply').textContent='';
  enabled(false);void status();
};
el('stop').onclick=()=>command('stop',{},el('stopReply'));
function renderCatalog(){
  const selected=el('name').value;
  const query=el('itemSearch').value.trim().toLocaleLowerCase('ko-KR');
  const matching=catalog.filter(name=>name.toLocaleLowerCase('ko-KR').includes(query));
  const placeholder=document.createElement('option');
  placeholder.value='';placeholder.textContent=query&&!matching.length?'검색 결과 없음':'품목을 선택하세요';
  el('name').replaceChildren(placeholder,...matching.map(name=>{
    const option=document.createElement('option');option.value=name;option.textContent=name;return option;
  }));
  el('name').value=matching.includes(selected)?selected:'';
  enabled(online);
}
el('catalog').onclick=async()=>{
  if(!online||busy||catalogLoading)return;
  catalogLoading=true;enabled(online);
  el('catalogReply').textContent='가공 품목을 불러오는 중';
  try{
    const result=JSON.parse(await api('/api/altering-catalog'));
    if(!result.names){el('catalogReply').textContent=result.message;return;}
    catalog=result.names;
    renderCatalog();
    el('catalogReply').textContent=catalog.length?'품목을 검색하고 선택하세요.':'선택 가능한 가공 품목이 없습니다.';
  }catch(e){el('catalogReply').textContent=e.message;}
  finally{catalogLoading=false;enabled(online);}
};
el('itemSearch').oninput=renderCatalog;
el('name').onchange=()=>enabled(online);
el('order').onclick=()=>{const name=el('name').value,n=Number(el('quantity').value);if(!catalog.includes(name)||!Number.isInteger(n)||n<1||n>1000000){el('reply').textContent='품목과 수량을 확인하세요.';return;}command('order',{Name:name,Quantity:n,Additional:el('mode').value==='additional'});};
el('queue').onclick=()=>status();
el('clearQueue').onclick=()=>{
  if(!online||busy||!canClearQueue)return;
  const revision=queueRevision;
  const message='가공 목록 '+orderCount+'건을 모두 비울까요? 시작 대기 요청도 지워지며, 다음 등록은 #1부터 시작합니다.';
  if(confirm(message))command('queue-clear',{QueueRevision:revision},el('queueReply'));
};
el('start').onclick=()=>{if(confirm('등록된 가공 작업을 시작할까요?'))command('queue-start',{},el('startReply'));};
function decodeResumeReply(reply){
  if(!reply)return null;
  try{return JSON.parse(reply);}catch{el('resumeReply').textContent=reply;return null;}
}
el('verifyResume').onclick=async()=>{
  if(!online||busy||!previousBatch)return;
  const batchId=previousBatch.id;
  const feedback=el('resumeReply');
  verifiedResume=null;
  feedback.textContent='게임 대기열과 완성 수량을 검증하는 중';
  let result=decodeResumeReply(await command('resume-verify',{BatchId:batchId},feedback));
  if(result?.consent){
    if(!confirm('현재 서버: '+result.realm+'\n이 서버에서 같은 캐릭터 하나로만 대량가공했나요? 다른 캐릭터라면 취소하세요.')){
      feedback.textContent='이어하기 검증을 취소했습니다.';
      return;
    }
    result=decodeResumeReply(await command('resume-verify',{BatchId:batchId,ResumeTicket:result.consent},feedback));
  }
  if(result?.ticket){
    verifiedResume={batchId,id:result.ticket};
    feedback.textContent=result.report;
    enabled(online);
  }
};
el('startResume').onclick=()=>{
  if(!online||busy||!verifiedResume||verifiedResume.batchId!==previousBatch?.id)return;
  if(!confirm('검증한 이전 가공 배치를 이어서 실행할까요?'))return;
  const id=verifiedResume.id;
  verifiedResume=null;
  void command('resume-start',{ResumeTicket:id},el('resumeReply'));
};
el('resetPrevious').onclick=()=>{
  if(!online||busy||!previousBatch)return;
  const batchId=previousBatch.id;
  const notice=el('previousDetail').textContent+'\n\n이어하기 목록에서 이 배치를 숨길까요? 게임 작업, 완성품, 원본 기록과 휴대폰 대기열은 보존됩니다.';
  if(confirm(notice))command('queue-reset-previous',{BatchId:batchId},el('resetReply'));
};
el('qstop').onclick=()=>command('queue-stop',{},el('startReply'));
for(const link of document.querySelectorAll('.today-work a')){
  const detail=el(link.hash.slice(1));
  if(detail instanceof HTMLDetailsElement)link.addEventListener('click',()=>{detail.open=true;});
}
el('abyss').onclick=()=>{const d=el('dungeon').value;if(confirm(d+' · 무한 반복으로 시작할까요?'))command('abyss-start',{Dungeon:d},el('abyssReply'));};
setInterval(status,5000);
""";
}
