'use strict';
const $ = id => document.getElementById(id);
const token = new URLSearchParams(location.hash.slice(1)).get('token');
history.replaceState(null, '', location.pathname);
const modes = {idle:'관측 대기',navigation:'이동',dialogue:'대화',interaction:'상호작용',puzzle:'퍼즐',stealth:'잠입',escape:'탈출',combat:'전투',recovery:'회복',cutscene:'영상',paused:'일시정지 확인'};
const labels = {host_started:'관제 시작',scene_registered:'상황 확인',thinking_started:'AI 판단 시작',action_planned:'행동 계획 등록',input_released:'입력 해제',action_result:'행동 결과 기록',stage_completed:'단계 완료',progress_lost:'진행 손실 확인',stop_requested:'입력 중지 요청',control_changed:'조작권 변경',settings_changed:'설정 변경',supervisor_overdue:'응답 기한 초과',attention_required:'확인 필요',pause_requested:'게임 정지 시도',direct_input_started:'화면 직접 조작',direct_input_returned:'직접 조작 반환',dialogue_checked:'대화 진행 확인'};
const alerts = {combat_manual:'전투 직접 수행 설정에 따라 조작권을 넘겼습니다.',combat_failures:'설정한 전투 실패 횟수에 도달했습니다.',resume_requires_observation:'AI가 새 화면을 확인하면 이어갈 수 있습니다.',restart_requires_observation:'재시작했습니다. 새 화면 확인이 필요합니다.',user_stop:'사용자가 입력을 중지했습니다.',user_request:'사용자 요청으로 조작권을 변경했습니다.',supervisor_overdue:'AI 응답 기한이 지났습니다.',input_error:'입력 실행 오류가 발생했습니다.',input_deadline_exceeded:'입력 실행 기한이 지났습니다.'};
let cursor = 0, initialized = false, dirty = false, busy = false, latest = null;
function duration(value) { if (value == null) return '—'; const n = Math.max(0, Math.floor(value)); return `${String(Math.floor(n/3600)).padStart(2,'0')}:${String(Math.floor(n/60)%60).padStart(2,'0')}:${String(n%60).padStart(2,'0')}`; }
async function rpc(method, params = {}) {
  if (!token) throw Error('연결 주소가 없습니다. 도구의 ui 명령으로 이 화면을 다시 여세요.');
  const response = await fetch('/rpc', {method:'POST', headers:{'Content-Type':'application/json',Authorization:`Bearer ${token}`},body:JSON.stringify({method,params}),signal:AbortSignal.timeout(4000)});
  const data = await response.json(); if (!data.ok) throw Error(data.error); return data.result;
}
function notify(text) { try { if ('Notification' in window && Notification.permission === 'granted') new Notification('Story Control', {body:text}); } catch { $('permissionState').textContent='이 환경에서 시스템 알림을 전달하지 못했습니다.'; } }
function render(s) {
  if (s.cursor < cursor) return;
  latest=s; $('title').textContent=s.plan_title; $('stage').textContent=s.stage_title || '새 화면 확인 대기'; $('mode').textContent=modes[s.mode] || s.mode;
  $('state').textContent=s.handoff_pending?'입력 해제 대기 · 아직 직접 조작하지 마세요':s.control==='user'?'사용자 조작 중':s.owner?'도구 실행 중':s.stopped?'실행 대기 / 중단':'AI 관측·판단 대기';
  $('observation').textContent=s.observation_wall?`${Math.max(0,Math.floor(Date.now()/1000-s.observation_wall))}초 전 관측 · ${s.input_enabled?'입력 허용':'무입력 모드'}`:`관측 없음 · ${s.input_enabled?'입력 허용':'무입력 모드'}`;
  $('elapsed').textContent=duration(s.session_seconds); $('stageTime').textContent=duration(s.steps[s.stage]?.seconds); $('wait').textContent=duration(s.control==='user'?null:s.supervisor_wait_seconds);
  $('progressText').textContent=`등록된 ${s.progress.total}단계 중 ${s.progress.completed}단계 완료`; $('progress').max=s.progress.total; $('progress').value=s.progress.completed;
  $('attention').textContent=(s.alert ? (alerts[s.alert] || `확인 필요: ${s.alert}`)+' ' : '')+(s.paused?'게임 일시정지 확인됨.':'게임 일시정지는 확인되지 않았습니다.');
  $('stop').disabled=busy; $('take').disabled=busy || s.control==='user'; $('resume').disabled=busy || !!s.owner || s.control==='agent'; $('save').disabled=busy;
  $('steps').replaceChildren(...s.plan_steps.map(step=>{const li=document.createElement('li'); const status=s.steps[step.id].status; li.textContent=`${status==='completed'?'✓':status==='active'?'진행 중 ·':status==='lost'?'진행 손실 ·':''} ${step.title}`; li.className=status==='completed'?'done':status==='active'?'active':''; return li;}));
  if (!dirty) { $('combat').value=s.settings.combat; $('failures').value=s.settings.failure_limit; $('notifications').checked=s.settings.notifications; }
  const events=s.events.filter(e=>e.sequence>cursor && labels[e.kind]);
  for (const event of events) { const li=document.createElement('li'); li.textContent=`${new Date(event.at).toLocaleTimeString()} · ${labels[event.kind]}${event.data.reason?' · '+(alerts[event.data.reason] || event.data.reason):''}`; $('events').prepend(li); }
  while ($('events').children.length>30) $('events').lastChild.remove();
  if (initialized && s.settings.notifications) {
    const completed=s.progress.completed===s.progress.total;
    const attention=events.some(e=>e.kind==='attention_required'||e.kind==='control_changed'&&e.data.target==='user'||e.kind==='input_released'&&e.data.error);
    if (completed && events.some(e=>e.kind==='stage_completed')) notify('등록된 모든 임무 단계를 완료했습니다.');
    else if (attention) notify(s.alert ? (alerts[s.alert] || s.alert) : '진행을 확인해 주세요.');
  }
  cursor=s.cursor; initialized=true;
}
async function poll() {
  try { const s=await rpc('status',{after_sequence:cursor}); $('connection').textContent='연결됨'; $('error').textContent=''; render(s); }
  catch(error) { $('connection').textContent='연결 끊김'; $('error').textContent=error.message; for (const id of ['stop','take','resume','save']) $(id).disabled=true; }
  finally { setTimeout(poll,1000); }
}
async function command(method,params) { busy=true; if(latest) renderButtons(); try { const state=await rpc(method,params); busy=false; render(state); $('error').textContent=''; } catch(e) { $('error').textContent=e.message; } finally { busy=false; } }
function renderButtons(){for(const id of ['stop','take','resume','save']) $(id).disabled=true;}
$('stop').onclick=()=>command('stop',{});
$('take').onclick=()=>command('set_control',{target:'user'});
$('resume').onclick=()=>command('set_control',{target:'agent'});
$('settings').oninput=()=>{dirty=true; $('saved').textContent='저장하지 않은 변경';};
$('settings').onsubmit=async event=>{event.preventDefault(); busy=true;renderButtons();try{const state=await rpc('configure',{combat:$('combat').value,failure_limit:Number($('failures').value),notifications:$('notifications').checked});dirty=false;busy=false;render(state);$('saved').textContent='저장됨';}catch(e){$('error').textContent=e.message;}finally{busy=false;}};
$('permission').onclick=async()=>{if(!('Notification' in window)){$('permissionState').textContent='이 브라우저는 알림을 지원하지 않습니다.';return;} const permission=await Notification.requestPermission(); $('permissionState').textContent=permission==='granted'?'알림 허용됨':'알림이 허용되지 않았습니다.';};
poll();
