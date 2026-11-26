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
<section><h2>자동가공</h2><label for="name">품목</label><input id="name" maxlength="120" placeholder="예: 가죽+">
<label for="quantity">수량</label><input id="quantity" type="number" min="1" max="1000000" value="100" inputmode="numeric">
<label for="mode">수량 방식</label><select id="mode"><option value="total">총 수량까지 채우기</option><option value="additional">추가 생산</option></select>
<div class="buttons"><button id="order" disabled>작업 등록</button><button id="queue" disabled>대기열 확인</button>
<button id="start" disabled>가공 시작</button><button id="qstop" disabled>가공 정지</button></div>
<p id="startReply" role="status"></p>
<div id="previousBatch"><pre id="previousDetail">PC 연결 후 이전 배치를 확인할 수 있습니다.</pre><button id="resetPrevious" class="danger" disabled>이전 배치 초기화</button><p id="resetReply" role="status"></p></div>
<button id="clearQueue" class="danger" disabled>가공 목록 전체 비우기 · #1부터</button>
<div id="orders">등록된 작업을 확인하세요.</div><p id="queueReply" role="status"></p></section>
<section><h2>어비스</h2><label for="dungeon">던전 선택</label><select id="dungeon"><option>허상의 정박지</option><option>광기의 동굴</option><option>흩어진 물길</option></select>
<p class="hint">기존 어비스 방식으로 무한 반복합니다. 종료하려면 전체 작업 중지를 누르세요.</p><button id="abyss" disabled>어비스 시작</button><p id="abyssReply" role="status"></p></section>
<section aria-live="polite"><h2>명령 응답</h2><pre id="reply">—</pre></section></main></body></html>
""";
    internal const string Css = """
*{box-sizing:border-box}body{margin:0;background:#f5f6f8;color:#232738;font:16px system-ui,sans-serif}
main{max-width:560px;margin:auto;padding:20px 16px 36px}header{display:flex;align-items:baseline;gap:12px;margin:8px 0 22px}
header strong{font-size:27px}header span,.hint{color:#656b7b;font-size:13px}section{background:white;border:1px solid #e1e4ec;border-radius:20px;padding:18px;margin-bottom:14px}
h2{font-size:17px;margin:0 0 14px}label{display:block;font-size:14px;margin:10px 0 5px}input,select,button{width:100%;min-height:44px;border-radius:11px;font:inherit}
input,select{border:1px solid #d9deeb;background:#f8f9fc;color:inherit;padding:8px 11px}button{border:0;background:#4b5ab8;color:white;padding:10px;margin-top:10px;font-weight:600;cursor:pointer}
button:disabled{opacity:.43;cursor:default}.buttons{display:grid;grid-template-columns:1fr 1fr;gap:8px}.hero{background:#24283b;color:#f7f8ff;border-color:#24283b}
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
function enabled(value){
  online=value;
  for(const id of controls)el(id).disabled=!value||busy;
  for(const button of el("orders").querySelectorAll("button"))button.disabled=!value||busy;
  el('resetPrevious').disabled=!value||busy||!previousBatch;
  el('clearQueue').disabled=!value||busy||!canClearQueue;
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
  renderPreviousBatch(state.previousBatch);
  queueRevision=state.queueRevision;
  canClearQueue=state.canClearQueue;
  orderCount=state.orders.length;
  renderQueue(state.orders,queueRevision);
  if(startWatch){
    const {operation,feedback,reply}=startWatch;
    const preparing=operation==='queue-start' ? state.phoneAltering && !state.altering : state.abyssPreparing;
    const active=operation==='queue-start' ? state.phoneAltering && state.altering : state.abyss;
    if(preparing || active){
      startWatch.observed=true;
      const needsAttention=operation==='queue-start' && state.orders.some(order=>order.state==='확인 필요');
      const stage=preparing?(operation==='queue-start'?'품목·재고 확인 중':'어비스 시작 준비 중'):needsAttention?'가공 실행 중 · 확인 필요 항목 있음':'실행 중';
      feedback.textContent=stage+' · '+state.detail;
    }else{
      feedback.textContent=startWatch.observed ? '실행 종료 · '+state.detail : reply+' · '+state.detail;
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
  const starting=operation==='queue-start'||operation==='abyss-start';
  if(starting){startWatch=null;stopWatch=null;}
  if(stopping||starting)feedback.textContent=stopping?'정지 요청 중':'시작 요청 중';
  try{
    if(!globalThis.isSecureContext||!crypto.randomUUID)throw Error('보안 연결(HTTPS 또는 PC 로컬 주소)이 필요합니다.');
    const request={Operation:operation,RequestId:crypto.randomUUID(),...fields};
    const reply=await api('/api/command',request);
    feedback.textContent=el('reply').textContent=reply;
    if(operation==='queue-cancel'||operation==='queue-clear')el('queueReply').textContent=reply;
    if(stopping && reply.includes('정지 요청 접수')){
      startWatch=null;
      stopWatch={operation,feedback};
    }
    if(starting && reply.includes('시작 요청 접수'))
      startWatch={operation,feedback,reply,observed:false};
  }catch(e){
    feedback.textContent=el('reply').textContent=e.message+' · 중복 실행하지 말고 PC 상태를 확인하세요.';
  }finally{
    busy=false;enabled(online);
    void status();
  }
}
el('connect').onclick=()=>{
  if(busy)return;
  poll?.abort();poll=null;
  key=el('key').value.trim();el('key').value='';
  online=false;queueSignature='';startWatch=null;stopWatch=null;
  enabled(false);void status();
};
el('stop').onclick=()=>command('stop',{},el('stopReply'));
el('order').onclick=()=>{const name=el('name').value.trim(),n=Number(el('quantity').value);if(!name||!Number.isInteger(n)||n<1||n>1000000){el('reply').textContent='품목과 수량을 확인하세요.';return;}command('order',{Name:name,Quantity:n,Additional:el('mode').value==='additional'});};
el('queue').onclick=()=>status();
el('clearQueue').onclick=()=>{
  if(!online||busy||!canClearQueue)return;
  const revision=queueRevision;
  const message='가공 목록 '+orderCount+'건을 모두 비울까요? 시작 대기 요청도 지워지며, 다음 등록은 #1부터 시작합니다.';
  if(confirm(message))command('queue-clear',{QueueRevision:revision},el('queueReply'));
};
el('start').onclick=()=>{if(confirm('등록된 가공 작업을 시작할까요?'))command('queue-start',{},el('startReply'));};
el('resetPrevious').onclick=()=>{
  if(!online||busy||!previousBatch)return;
  const batchId=previousBatch.id;
  const notice=el('previousDetail').textContent+'\n\n이어하기 목록에서 이 배치를 숨길까요? 게임 작업, 완성품, 원본 기록과 휴대폰 대기열은 보존됩니다.';
  if(confirm(notice))command('queue-reset-previous',{BatchId:batchId},el('resetReply'));
};
el('qstop').onclick=()=>command('queue-stop',{},el('startReply'));
el('abyss').onclick=()=>{const d=el('dungeon').value;if(confirm(d+' · 무한 반복으로 시작할까요?'))command('abyss-start',{Dungeon:d},el('abyssReply'));};
setInterval(status,5000);
""";
}
