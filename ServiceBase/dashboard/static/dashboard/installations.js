import {requestError} from './request-error.mjs?v=plugin-lifecycle13';
import {recentInstallationPlans,stepLabel} from './installation-plan.mjs?v=mt-packages12';
import {onProjectSelected} from './project-selection.mjs';
import {installationLabel,installationTone,installationProgress,installationStepTone,installationKind} from './installation-history.mjs?v=11';
const $ = id => document.getElementById(id);
const text = (tag, value = '', className = '') => {
  const node = document.createElement(tag); node.textContent = value; node.className = className; return node;
};
const retryDrafts = new Map(), pendingActions = new Set();
let project = null, generation = 0, jobs = [], fetching = false, refreshAgain = false;
let renderedSignature = '', detailSignature = '', selectedJob = null, deletingJob = null;
const dialog = $('installation-detail');
function headers() { return {'Content-Type':'application/json','X-CSRFToken':document.querySelector('[name=csrfmiddlewaretoken]')?.value || ''}; }
async function api(url, options = {}) {
  const response = await fetch(url, {...options, signal:AbortSignal.timeout(15000)});
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || `请求失败 (${response.status})`);
  return data;
}
function error(message) {
  const area = dialog.open ? $('installation-detail-error') : $('installation-error');
  area.textContent = message; area.hidden = !message;
}
function base() { return `/api/projects/${encodeURIComponent(project.id)}/installations/`; }
const actionKey = job => `${generation}:${job.id}`;
const jobName = job => job.target.display_name || job.target.name;
const date = value => new Date(value).toLocaleString();
function button(label, action, className = 'secondary') {
  const node = text('button',label,className); node.type = 'button'; node.onclick = action; return node;
}
function statusBadge(state, busy = false) {
  const node = text('span',installationLabel(state),'installation-state');
  node.dataset.busy = String(busy); return node;
}
function progress(job, compact = false) {
  const p = installationProgress(job), area = text('div','','installation-progress');
  const label = text('div','','installation-progress-label');
  label.append(text('span',compact ? '步骤进度' : `已完成 ${p.completed} 步 · 已跳过 ${p.skipped} 步`),text('strong',`${p.finished} / ${p.total}`));
  const bar = document.createElement('progress'); bar.max = Math.max(1,p.total); bar.value = p.finished;
  bar.setAttribute('aria-label',`${jobName(job)}：已完成或跳过 ${p.finished} 步，共 ${p.total} 步`);
  area.append(label,bar); return area;
}
function closeDetail() {
  const previous = selectedJob; selectedJob = deletingJob = null; detailSignature = '';
  dialog.close();
  [...$('installation-jobs').querySelectorAll('[data-job-id]')].find(node => node.dataset.jobId === previous)?.focus({preventScroll:true});
}
$('installation-detail-close').onclick = closeDetail;
dialog.addEventListener('cancel',event => { event.preventDefault(); closeDetail(); });
function openDetail(id, deleting = false) {
  selectedJob = id; deletingJob = deleting ? id : null; detailSignature = '';
  dialog.scrollTop = 0;
  $('installation-detail-error').hidden = true;
  renderDetail();
  if (!dialog.open) dialog.showModal();
  dialog.scrollTop = 0;
  $('installation-detail-close').focus();
  if (deleting) $('installation-detail-content').lastElementChild?.scrollIntoView({block:'nearest'});
}

async function act(job, suffix, options, close = false) {
  const key = actionKey(job);
  if (pendingActions.has(key)) return;
  const revision = generation, url = `${base()}${job.id}/${suffix}`;
  pendingActions.add(key); error(''); renderCards(); renderDetail();
  try {
    await api(url, {headers:headers(),...options});
    if (revision !== generation) return;
    if (close && selectedJob === job.id) closeDetail();
    await refresh();
  } catch (e) { if (revision === generation) error(requestError(e)); }
  finally {
    pendingActions.delete(key);
    if (revision === generation) { renderCards(); renderDetail(); }
  }
}

function renderCards() {
  const area = $('installation-jobs'), scroll = area.scrollLeft;
  const focused = document.activeElement?.dataset.jobId;
  area.replaceChildren();
  if (!jobs.length) area.append(text('p',project ? '暂无安装记录。在工程包或插件详情中开始安装。' : '选择工程后查看安装计划。','installation-empty'));
  for (const job of jobs) {
    const card = text('article','','installation-card'); card.dataset.tone = installationTone(job); card.setAttribute('role','listitem');
    const open = button('',() => openDetail(job.id),'installation-card-open');
    open.dataset.jobId = job.id; open.setAttribute('aria-label',`查看${installationKind(job.target)}：${jobName(job)}，${installationLabel(job.display_state)}`);
    const heading = text('div','','installation-card-heading');
    heading.append(text('span',installationKind(job.target)),statusBadge(job.display_state,['running','verifying'].includes(job.display_state)));
    const name = text('h3',jobName(job)); name.title = jobName(job);
    const current = job.steps.find(step => step.attempt_id === job.attempt_id);
    const activity = job.active && current ? `${stepLabel(current.target)} · ${current.target.name}` : job.report.message || installationLabel(job.display_state);
    open.append(heading,name,text('p',activity,'installation-card-activity'),progress(job,true),text('span','查看计划详情 →','installation-card-link'));
    const footer = text('div','','installation-card-footer'); footer.append(text('time',date(job.created_at)));
    if (job.can_delete) {
      const remove = button('删除记录',() => openDetail(job.id,true),'installation-delete-link');
      remove.setAttribute('aria-label',`删除 ${jobName(job)} 的记录`); remove.disabled = pendingActions.has(actionKey(job)); footer.append(remove);
    }
    card.append(open,footer); area.append(card);
  }
  area.scrollLeft = scroll;
  if (focused && !dialog.open) [...area.querySelectorAll('[data-job-id]')].find(node => node.dataset.jobId === focused)?.focus({preventScroll:true});
}

function renderDetail() {
  if (!selectedJob) return;
  const job = jobs.find(item => item.id === selectedJob);
  if (!job) { closeDetail(); return; }
  const pending = pendingActions.has(actionKey(job));
  const signature = JSON.stringify([job,pending,deletingJob,jobs.some(item => item.active)]);
  if (signature === detailSignature) return;
  detailSignature = signature;
  const content = $('installation-detail-content'), scroll = dialog.scrollTop;
  const focusKey = content.contains(document.activeElement) ? document.activeElement.dataset.focusKey : '';
  const selection = focusKey === 'retry-reason' ? [document.activeElement.selectionStart,document.activeElement.selectionEnd] : null;
  const expanded = [...content.querySelectorAll('details[open]')].map(node => node.dataset.section);
  content.replaceChildren();
  $('installation-detail-title').textContent = jobName(job);
  $('installation-detail-context').textContent = `${installationKind(job.target)} · ${project.name}`;
  dialog.dataset.tone = installationTone(job);
  const summary = text('section','','installation-detail-summary');
  const heading = text('div','','installation-summary-heading');
  heading.append(statusBadge(job.display_state,['running','verifying'].includes(job.display_state)),text('small',`创建于 ${date(job.created_at)}`));
  summary.append(heading,progress(job));
  if (job.report.message) summary.append(text('p',job.report.message,'installation-result'));
  if (job.diagnostic) summary.append(text('p',job.diagnostic,'installation-result'));
  content.append(summary);

  const actions = text('div','','installation-detail-actions');
  if (job.target.operation === 'plan' && job.active && !job.cancel_requested) {
    for (const [action,label] of [[job.paused?'resume':'pause',job.paused?'继续后续步骤':'暂停后续步骤'],['cancel','取消后续步骤']]) {
      const control = button(label,() => act(job,'control/',{method:'POST',body:JSON.stringify({action})}));
      control.disabled = pending; control.dataset.focusKey = action; actions.append(control);
    }
  }
  if (job.active && job.state !== 'waiting_input') {
    const check = button('重新核对',() => act(job,'recheck/',{method:'POST',body:'{}'}));
    check.disabled = pending; check.dataset.focusKey = 'recheck'; actions.append(check);
  }
  if (actions.childElementCount) {
    content.append(actions,text('p','取消或暂停会等待当前步骤结束；重新核对只检查实际状态。','installation-help'));
  }
  if (job.can_retry && (!jobs.some(item => item.active) || job.target.operation === 'plan')) {
    const key = `${project.id}:${job.attempt_id}`;
    if (!retryDrafts.has(key)) retryDrafts.set(key,{reason:'',key:crypto.randomUUID()});
    const draft = retryDrafts.get(key), retryArea = text('div','','installation-retry');
    const label = text('label','修复内容及重试原因'); label.htmlFor = 'installation-retry-reason';
    const reason = document.createElement('input'); reason.id = 'installation-retry-reason'; reason.dataset.focusKey = 'retry-reason';
    reason.placeholder = '填写修复内容后重试'; reason.maxLength = 500; reason.value = draft.reason; reason.disabled = pending;
    reason.oninput = () => { draft.reason = reason.value; draft.key = crypto.randomUUID(); };
    const retry = button('记录原因并重试',() => {
      if (!draft.reason.trim()) { error('请填写修复内容及重试原因。'); reason.focus(); return; }
      act(job,'retry/',{method:'POST',body:JSON.stringify({request_key:draft.key,attempt_id:job.attempt_id,reason:draft.reason.trim()})});
    });
    retry.disabled = pending; retry.dataset.focusKey = 'retry'; retryArea.append(label,reason,retry); content.append(retryArea);
  }

  const stepHeading = text('div','','installation-steps-heading');
  stepHeading.append(text('h3','执行步骤'));
  if (job.active) stepHeading.append(button('定位当前步骤',() => list.querySelector('[aria-current]')?.scrollIntoView({block:'center'}),'installation-jump'));
  else stepHeading.append(text('span','按依赖顺序执行'));
  content.append(stepHeading);
  const list = text('ol','','installation-step-list');
  for (const [index,step] of job.steps.entries()) {
    const item = text('li','','installation-step'); item.dataset.tone = installationStepTone(step,job);
    if (job.active && step.attempt_id === job.attempt_id && !['succeeded','skipped','cancelled'].includes(step.state)) item.setAttribute('aria-current','step');
    const icon = text('span',step.state==='succeeded'?'✓':step.state==='skipped'?'−':String(index+1),'installation-step-number');
    const body = text('div','','installation-step-body'), top = text('div','','installation-step-heading');
    const heading = text('div'); heading.append(text('small',stepLabel(step.target)),text('strong',step.target.display_name || step.target.name));
    const working = ['running','verifying','accepted'].includes(step.state) && job.active;
    top.append(heading,statusBadge(step.state,working)); body.append(top);
    const bar = document.createElement('progress'); bar.max = 1;
    if (!working) bar.value = ['succeeded','skipped'].includes(step.state) ? 1 : 0;
    bar.setAttribute('aria-label',`第 ${index+1} 步：${installationLabel(step.state)}`);
    body.append(bar);
    if (step.destination_root) body.append(text('small',`导入目录：${step.destination_root}`));
    if (step.report.message || step.target.reason) body.append(text('p',step.report.message || step.target.reason));
    item.append(icon,body); list.append(item);
  }
  content.append(list);
  const metadata = text('details','','installation-metadata'); metadata.dataset.section = 'metadata';
  metadata.append(text('summary','记录信息'),text('p',`任务编号：${job.id}`),text('p',`最后进度：${date(job.updated_at)} · 第 ${job.attempt_number} 次尝试`));
  if (job.target.operation !== 'plan') metadata.append(text('p',job.target.identifier));
  for (const message of [...(job.target.warnings || []),...(job.target.conflicts || [])]) metadata.append(text('p',message));
  if (job.attempt_number > 1) {
    metadata.append(text('h4','当前步骤的尝试记录（最近 10 次）'));
    for (const attempt of job.attempts) metadata.append(text('p',`第 ${attempt.number} 次 · ${installationLabel(attempt.state)}${attempt.retry_reason?' · '+attempt.retry_reason:''}`));
  }
  metadata.open = expanded.includes('metadata'); content.append(metadata);

  if (job.can_delete) {
    const footer = text('div','','installation-delete-area');
    if (deletingJob === job.id) {
      footer.append(text('p','从历史列表删除这条记录？工程中的包和插件不受影响。'));
      const remove = button(pending?'正在删除…':'确认删除记录',() => act(job,'',{method:'DELETE'},true),'secondary installation-danger');
      const keep = button('保留记录',() => { deletingJob = null; renderDetail(); });
      remove.disabled = keep.disabled = pending; footer.append(remove,keep);
    } else {
      const remove = button('删除记录',() => { deletingJob = job.id; renderDetail(); content.lastElementChild.scrollIntoView({block:'nearest'}); },'secondary installation-danger');
      remove.disabled = pending; footer.append(remove);
    }
    content.append(footer);
  }
  dialog.scrollTop = scroll;
  if (focusKey) {
    const focus = [...content.querySelectorAll('[data-focus-key]')].find(node => node.dataset.focusKey === focusKey);
    if (focus && !focus.disabled) { focus.focus({preventScroll:true}); if (selection) focus.setSelectionRange(...selection); }
  }
}

async function refresh() {
  if (!project) return;
  if (fetching) { refreshAgain = true; return; }
  fetching = true; const revision = generation, url = base();
  try {
    const data = await api(url);
    if (revision !== generation) return;
    error('');
    $('installation-target').textContent = `目标工程：${project.name} · ${data.executor_online?'独立执行器在线':'独立执行器未连接'}${data.executor_diagnostic?' · '+data.executor_diagnostic:''}`;
    const signature = JSON.stringify([project.id,data]);
    if (signature === renderedSignature) return;
    renderedSignature = signature; jobs = data.jobs;
    renderCards(); renderDetail();
    window.dispatchEvent(new Event('installation-status-changed'));
  } catch (e) { if (revision === generation) error(requestError(e)); }
  finally {
    fetching = false;
    if (refreshAgain || revision !== generation) { refreshAgain = false; refresh(); }
  }
}
onProjectSelected(value => {
  if (project && value && project.id === value.id && project.session === value.session) { project = value; refresh(); return; }
  if (dialog.open) closeDetail();
  project = value; generation++; renderedSignature = ''; jobs = [];
  $('installation-error').hidden = true; $('installation-recent').disabled = !project;
  $('installation-target').textContent = project ? `目标工程：${project.name} · 正在读取状态…` : '请先选择工程。';
  renderCards(); refresh();
});
$('installation-recent').onclick = () => recentInstallationPlans().catch(e => error(requestError(e)));
setInterval(refresh,5000);
window.addEventListener('installation-created',() => { refresh(); });
