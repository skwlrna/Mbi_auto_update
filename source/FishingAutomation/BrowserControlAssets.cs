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
<section class="hero"><h2>현재 작업</h2><pre id="status">연결 대기</pre><button class="danger" id="stop" disabled>전체 작업 중지</button></section>
<section><h2>자동가공</h2><label for="name">품목</label><input id="name" maxlength="120" placeholder="예: 가죽+">
<label for="quantity">수량</label><input id="quantity" type="number" min="1" max="1000000" value="100" inputmode="numeric">
<label for="mode">수량 방식</label><select id="mode"><option value="total">총 수량까지 채우기</option><option value="additional">추가 생산</option></select>
<div class="buttons"><button id="order" disabled>작업 등록</button><button id="queue" disabled>대기열 확인</button>
<button id="start" disabled>가공 시작</button><button id="qstop" disabled>가공 정지</button></div>
<pre id="orders">등록된 작업을 확인하세요.</pre><label for="number">취소할 대기 작업 번호</label><input id="number" type="number" min="1" inputmode="numeric"><button id="cancel" disabled>대기 작업 취소</button></section>
<section><h2>어비스</h2><label for="dungeon">던전 선택</label><select id="dungeon"><option>허상의 정박지</option><option>광기의 동굴</option><option>흩어진 물길</option></select>
<p class="hint">기존 어비스 방식으로 무한 반복합니다. 종료하려면 전체 작업 중지를 누르세요.</p><button id="abyss" disabled>어비스 시작</button></section>
<section aria-live="polite"><h2>명령 응답</h2><pre id="reply">—</pre></section></main></body></html>
""";
    internal const string Css = """
*{box-sizing:border-box}body{margin:0;background:#f5f6f8;color:#232738;font:16px system-ui,sans-serif}
main{max-width:560px;margin:auto;padding:20px 16px 36px}header{display:flex;align-items:baseline;gap:12px;margin:8px 0 22px}
header strong{font-size:27px}header span,.hint{color:#656b7b;font-size:13px}section{background:white;border:1px solid #e1e4ec;border-radius:20px;padding:18px;margin-bottom:14px}
h2{font-size:17px;margin:0 0 14px}label{display:block;font-size:14px;margin:10px 0 5px}input,select,button{width:100%;min-height:44px;border-radius:11px;font:inherit}
input,select{border:1px solid #d9deeb;background:#f8f9fc;color:inherit;padding:8px 11px}button{border:0;background:#4b5ab8;color:white;padding:10px;margin-top:10px;font-weight:600;cursor:pointer}
button:disabled{opacity:.43;cursor:default}.buttons{display:grid;grid-template-columns:1fr 1fr;gap:8px}.hero{background:#24283b;color:#f7f8ff;border-color:#24283b}
.danger{background:#b52b40}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:14px/1.6 system-ui,sans-serif;margin:10px 0}
@media(prefers-color-scheme:dark){body{background:#17171d;color:#eff1fa}section{background:#23252e;border-color:#3e4252}input,select{background:#303340;border-color:#4a4e60}.hint,header span{color:#aab3c8}}
""";
    internal const string Js = """
'use strict';
const el=id=>document.getElementById(id);
const controls=['stop','order','queue','start','qstop','cancel','abyss'];
let key='', busy=false, online=false;
function enabled(value){online=value;for(const id of controls)el(id).disabled=!value||busy;}
async function api(path,payload){
  const options={cache:'no-store',headers:{Authorization:'Bearer '+key}};
  if(payload){options.method='POST';options.headers['Content-Type']='application/json';options.body=JSON.stringify(payload);}
  const r=await fetch(path,options); const data=await r.json();
  if(!r.ok)throw Error(data.message||'요청 실패');
  return data.message;
}
async function status(){
  if(!key||busy)return;
  busy=true;enabled(online);
  try{el('status').textContent=await api('/api/status');online=true;}
  catch(e){el('status').textContent=e.message;online=false;}
  finally{busy=false;enabled(online);}
}
async function command(operation,fields={}){
  if(!online||busy)return;
  busy=true;enabled(online);
  try{
    if(!globalThis.isSecureContext||!crypto.randomUUID)throw Error('보안 연결(HTTPS 또는 PC 로컬 주소)이 필요합니다.');
    const request={Operation:operation,RequestId:crypto.randomUUID(),...fields};
    el('reply').textContent=await api('/api/command',request);
    if(operation.startsWith('queue-')||operation==='order')el('orders').textContent=await api('/api/queue');
    el('status').textContent=await api('/api/status');
  }catch(e){el('reply').textContent=e.message+' · 중복 실행하지 말고 PC 상태를 확인하세요.';}
  finally{busy=false;enabled(online);}
}
el('connect').onclick=()=>{if(busy)return;key=el('key').value.trim();el('key').value='';online=false;enabled(false);status();};
el('stop').onclick=()=>command('stop');
el('order').onclick=()=>{const name=el('name').value.trim(),n=Number(el('quantity').value);if(!name||!Number.isInteger(n)||n<1||n>1000000){el('reply').textContent='품목과 수량을 확인하세요.';return;}command('order',{Name:name,Quantity:n,Additional:el('mode').value==='additional'});};
el('queue').onclick=async()=>{if(!online||busy)return;busy=true;enabled(online);try{el('orders').textContent=await api('/api/queue');}catch(e){el('reply').textContent=e.message;}finally{busy=false;enabled(online);}};
el('start').onclick=()=>{if(confirm('등록된 가공 작업을 시작할까요?'))command('queue-start');};
el('qstop').onclick=()=>command('queue-stop');
el('cancel').onclick=()=>{const n=Number(el('number').value);if(Number.isSafeInteger(n)&&n>0)command('queue-cancel',{Number:n});};
el('abyss').onclick=()=>{const d=el('dungeon').value;if(confirm(d+' · 무한 반복으로 시작할까요?'))command('abyss-start',{Dungeon:d});};
setInterval(status,5000);
""";
}
