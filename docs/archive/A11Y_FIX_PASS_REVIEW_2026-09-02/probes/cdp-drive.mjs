import { spawn } from 'node:child_process';
const S='.';
const chrome = spawn('/home/cody/.cache/ms-playwright/chromium-1187/chrome-linux/chrome',
  ['--headless=new','--no-sandbox','--disable-gpu','--allow-file-access-from-files','--remote-debugging-port=9333','about:blank'],{stdio:'ignore'});
const sleep=(ms)=>new Promise(r=>setTimeout(r,ms));
let targets; for (let i=0;i<40;i++){ try{ targets=await (await fetch('http://127.0.0.1:9333/json')).json(); break;}catch{ await sleep(250);} }
const ws=new WebSocket(targets.find(t=>t.type==='page').webSocketDebuggerUrl);
await new Promise(r=>ws.onopen=r);
let id=0; const pending={};
ws.onmessage=(m)=>{const d=JSON.parse(m.data); if(d.id&&pending[d.id]){pending[d.id](d); delete pending[d.id];}};
const send=(method,params={})=>new Promise(res=>{const i=++id; pending[i]=res; ws.send(JSON.stringify({id:i,method,params}));});
const ev=async(expr)=>(await send('Runtime.evaluate',{expression:expr,returnByValue:true})).result?.result?.value;
const key=async(k,shift=false)=>{
  const codes={Tab:{code:'Tab',vk:9},ArrowDown:{code:'ArrowDown',vk:40},PageDown:{code:'PageDown',vk:34},End:{code:'End',vk:35},ArrowRight:{code:'ArrowRight',vk:39}};
  const c=codes[k]; const mods=shift?8:0;
  await send('Input.dispatchKeyEvent',{type:'rawKeyDown',key:k,code:c.code,windowsVirtualKeyCode:c.vk,nativeVirtualKeyCode:c.vk,modifiers:mods});
  await send('Input.dispatchKeyEvent',{type:'keyUp',key:k,code:c.code,windowsVirtualKeyCode:c.vk,nativeVirtualKeyCode:c.vk,modifiers:mods});
  await sleep(120);
};
await send('Page.enable'); await send('Runtime.enable');
await send('Page.navigate',{url:`file://${S}/cdp-page.html`}); await sleep(1200);
const out=[];
const rawTab=async()=>{await send('Input.dispatchKeyEvent',{type:'rawKeyDown',key:'Tab',code:'Tab',windowsVirtualKeyCode:9,nativeVirtualKeyCode:9}); await send('Input.dispatchKeyEvent',{type:'keyUp',key:'Tab',code:'Tab',windowsVirtualKeyCode:9,nativeVirtualKeyCode:9}); await sleep(150);};
for (const pos of ['fixed','absolute']) {
  await send('Page.navigate',{url:`file://${S}/cdp-page2.html`}); await sleep(1000);
  out.push('setup: '+await ev(`window.setup('${pos}')`));
  await rawTab(); await rawTab();
  out.push(await ev("window.flog.join(String.fromCharCode(10)+\"  \")")+'\n  final active='+await ev('document.activeElement.id'));
}
console.log(out.join('\n'));
ws.close(); chrome.kill();
