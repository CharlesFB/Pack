import {requestError} from './request-error.mjs?v=plugin-lifecycle13';
import {openInstallationPlan} from './installation-plan.mjs?v=mt-packages12';
import {selectProject} from './project-selection.mjs';
let packageTab = "catalog";  // 当前包面板显示完整目录还是工程依赖树。
import {indexPackages, describeNode} from './dependency-graph.mjs';
import {packageDisplayName, packageFlags, packageKey, dependencyPackages, packageTags, packageActions, packageAtTag} from './package-display.mjs?v=package-origin10';
const selectedTags = new Map();  // 按工程、编辑器会话及目录项保存用户选择，避免不同设备的标签选择串联。
let selectedPackage = null, detailTab = 'info', packageViewSignature = '';  // 当前目录项、详情标签页，以及避免无效 DOM 重建的渲染签名。
const $ = id => document.getElementById(id);  // 按 ID 查询工作台控件的简写。
let projects = [], selectedId = null, sending = false, loading = false;  // 当前分页工程、选中工程，以及消息发送和列表加载状态。
const cards = new Map(), mtpStates = new Map();  // 复用工程卡片 DOM；mtpStates 保存各工程会话的快照和展开路径。
const deleting = new Set();  // 正在移除的工程 ID 集合，防止重复提交删除请求。
let listRevision = 0, page = 1, pages = 1, total = 0;  // 列表操作版本用于丢弃过时响应；其余字段为当前分页统计。
let activeLoads = 0;  // 正在执行的受限快照读取数量，最大为四个。
const loadQueue = [];  // 等待获得读取名额的 Promise 唤醒函数队列。
/**
 * 限制并发快照读取数量，将超出上限的读取排队。
 */
async function limitedLoad(
  work // 受并发限制的异步读取函数
) {
  if (activeLoads >= 4) await new Promise(resolve => loadQueue.push(resolve));
  else activeLoads++;
  try { return await work(); }
  finally { const next = loadQueue.shift(); if (next) next(); else activeLoads--; }
}
/**
 * 轮询任务结果直至完成或超时，不重复提交执行请求。
 */
async function waitTask(
  id // 目标工程或任务 ID
) {
  const deadline = Date.now() + 65000;
  while (Date.now() < deadline) {
    await new Promise(resolve => setTimeout(resolve, 1500));
    const res = await fetch(`/api/tasks/${encodeURIComponent(id)}/`, {signal: AbortSignal.timeout(20000)});
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || '任务查询失败');
    if (data.state === 'done') { if (data.response?.ok === false) throw new Error(data.response.feedback || '工程执行失败'); return data; }
    if (data.state === 'expired') throw new Error('任务已过期或工程会话已变化');
  }
  throw new Error('等待任务超时；任务可能仍在处理，请勿重复发送');
}
const notice = textNode('div', 'project-notice', '');
notice.hidden = true; notice.setAttribute('role', 'status');
$('project-list').before(notice);
/**
 * 从页面表单读取 CSRF token，用于浏览器写请求。
 */
function csrfHeaders() {
  return {'X-CSRFToken': document.querySelector('[name=csrfmiddlewaretoken]').value};
}
/**
 * 移除工程当前会话记录并提供撤销入口，不删除工程文件。
 */
async function deleteProject(
  id // 目标工程或任务 ID
) {
  const project = projects.find(p => p.instance_id === id);
  if (!project || deleting.has(id)) return;
  deleting.add(id); render();
  try {
    const response = await fetch(`/api/projects/${encodeURIComponent(id)}/`, {
      method:'DELETE', headers:csrfHeaders(), signal:AbortSignal.timeout(20000)
    });
    const result = await response.json();
    if (!response.ok) throw new Error(result.error || `HTTP ${response.status}`);
    listRevision++;
    projects = projects.filter(p => p.instance_id !== id);
    if (selectedId === id) {
      selectedId = projects.find(p => p.online)?.instance_id;
      $('response-meta').textContent = '尚未发送';
      $('response-output').textContent = '发送一条消息，查看工程返回结果。';
      $('send-error').hidden = true;
    }
    notice.replaceChildren(textNode('span', '', `已从列表移除「${project.project_name}」。重启该 Unity 工程后会重新出现。`));
    const undo = textNode('button', 'secondary', '撤销'); undo.type = 'button';
    undo.onclick = async () => {
      undo.disabled = true;
      try {
        const response = await fetch(`/api/projects/${encodeURIComponent(id)}/restore/`, {
          method:'POST', headers:csrfHeaders(), signal:AbortSignal.timeout(20000)
        });
        if (!response.ok) throw new Error('恢复失败，请重试');
        listRevision++; notice.hidden = true; await refresh();
      } catch (error) { undo.disabled = false; notice.firstChild.textContent = requestError(error); }
    };
    notice.append(undo); notice.hidden = false;
  } catch (error) {
    notice.replaceChildren(textNode('span', '', `移除失败：${requestError(error)}`)); notice.hidden = false;
  } finally { deleting.delete(id); render(); }
}
/**
 * 创建仅填充文本的 DOM 节点，避免将包元数据作为 HTML 执行。
 */
function textNode(
  tag, // 所选版本标签或 DOM 标签名
  cls, // DOM 样式类名
  text // 作为纯文本显示的内容
) {
  const el = document.createElement(tag); el.className = cls; el.textContent = text; return el;
}
/**
 * 按工程会话复用快照状态，编辑器会话变化时重新初始化。
 */
function stateFor(
  p // Unity 工程信息
) {
  const generation = `${p.session_id}:${p.port}`;
  let state = mtpStates.get(p.instance_id);
  if (!state || state.generation !== generation) {
    state = {generation, checked: false, loading: false, data: null, error: '', paths: new Set()};
    mtpStates.set(p.instance_id, state);
  }
  return state;
}
/**
 * 创建工程卡片及其交互控件，供后续局部刷新复用。
 */
function makeCard(
  p // Unity 工程信息
) {
  const card = textNode('article', 'project', '');
  const select = textNode('button', 'project-select', ''); select.type = 'button';
  select.onclick = () => { selectedId = p.instance_id; selectedPackage = null; detailTab = 'info'; render(); };
  const mtp = textNode('div', 'mtp-panel', ''), toolbar = textNode('div', 'mtp-toolbar', '');
  const status = textNode('span', 'mtp-status', '等待检查 MTP');
  const refresh = textNode('button', 'mtp-refresh secondary', '刷新依赖'); refresh.type = 'button';
  refresh.onclick = () => loadMtp(p.instance_id, true);
  const remove = textNode('button', 'project-delete', '删除记录'); remove.type = 'button';
  remove.title = '只移除列表记录；不删除工程文件。重启 Unity 后可重新发现。';
  remove.setAttribute('aria-label', `删除记录：${p.project_name}`);
  remove.onclick = () => deleteProject(p.instance_id);
  const actions = textNode('div', 'project-actions', ''); actions.append(refresh, remove);
  toolbar.append(status, actions);
  const panel = document.createElement('details'), summary = textNode('summary', 'mtp-summary', '工程包依赖');
  const body = textNode('div', 'mtp-body', ''); panel.append(summary, body);
  panel.ontoggle = () => { if (panel.open) loadMtp(p.instance_id); };
  mtp.append(toolbar); card.append(select, mtp);
  const view = {card, select, panel, summary, body, status, refresh, remove};
  cards.set(p.instance_id, view); return view;
}
/**
 * 更新工程列表、选中状态和消息目标，仅加载选中工程的快照。
 */
function render() {
  const list = $('project-list');
  for (const [id, view] of cards) if (!projects.some(p => p.instance_id === id)) {
    view.card.remove(); cards.delete(id); mtpStates.delete(id);
  }
  $('empty').hidden = projects.length !== 0;
  $('online-count').textContent = projects.filter(p => p.online).length;
  $('total-count').textContent = total;
  $('page-label').textContent = `${page} / ${pages}`;
  $('prev-page').disabled = page <= 1; $('next-page').disabled = page >= pages;
  for (const p of projects) {
    const view = cards.get(p.instance_id) || makeCard(p);
    view.remove.disabled = deleting.has(p.instance_id);
    if (p.online && view.online === false) stateFor(p).checked = false;
    view.online = p.online;
    if (!view.card.parentNode) list.append(view.card);
    view.card.className = `project ${p.online ? '' : 'offline'} ${p.instance_id === selectedId ? 'selected' : ''}`;
    view.select.setAttribute('aria-pressed', String(p.instance_id === selectedId));
    const tagSelectionKey = JSON.stringify([selectedId, p?.session_id, selectedPackage]);
  const signature = JSON.stringify({...p, last_seen: null});
    if (view.signature !== signature) {
      view.signature = signature;
      const top = textNode('div', 'project-top', '');
      top.append(textNode('span', 'project-icon', 'U'), textNode('span', 'project-name', p.project_name));
      const status = textNode('span', 'status', '');
      status.append(textNode('i', 'dot', ''), document.createTextNode(p.online ? '在线' : '离线')); top.append(status);
      const details = textNode('div', 'details', '');
      [`Unity ${p.unity_version}`, p.machine_name, p.transport === 'pull' ? '主动连接' : `:${p.port}`, `PID ${p.process_id}`].forEach(t => details.append(textNode('span', '', t)));
      view.identity = textNode('div', 'identity', '');
      view.select.replaceChildren(top, textNode('div', 'path', p.project_path), details, view.identity);
      renderMtp(p);
    }
    view.identity.textContent = `ID ${p.instance_id.slice(0, 12)} · 最近心跳 ${new Date(p.last_seen).toLocaleTimeString()}`;
    const state = stateFor(p);
    if (p.instance_id === selectedId && !state.loading && (!state.checked || state.digest !== p.snapshot_digest)) loadMtp(p.instance_id);
  }
  const p = projects.find(p => p.instance_id === selectedId);
  selectProject(p);
  $('target-name').textContent = p ? p.project_name : '请先选择一个在线工程';
  $('target-endpoint').textContent = p ? `${p.transport === 'pull' ? '主动连接 · 任务队列' : `本机端口 ${p.port}`} · ${p.online ? '在线' : '已离线'}` : 'GET / POST → Unity 本地端口';
  $('send').disabled = sending || !p?.online;
  renderPackageWorkspace();
}
/**
 * 构造可展开的依赖节点，延迟生成子树并保留展开路径。
 */
function packageNode(
  graph, // 依赖图索引及引用计数
  name, // 依赖包名
  ancestors, // 当前路径的祖先包名，用于检测循环
  requestedTag, // 父节点要求的依赖标签
  state // 当前工程的快照和展开状态
) {
  const {pkg, missing, cycle, shared, differs} = describeNode(graph, name, ancestors, requestedTag);
  const li = document.createElement('li'), label = textNode('span', 'package-label', '');
  label.append(textNode('span', 'package-name', pkg ? packageDisplayName(pkg) : name), textNode('span', 'package-tag', pkg?.tag || requestedTag || '无 Tag'));
  if (pkg?.is_local) label.append(textNode('span', 'tree-badge', '本地'));
  if (shared) label.append(textNode('span', 'tree-badge', '共享依赖'));
  if (cycle) label.append(textNode('span', 'tree-warning', '循环引用'));
  if (missing) label.append(textNode('span', 'tree-warning', '快照中缺失'));
  if (differs) label.append(textNode('span', 'tree-warning', `要求 ${requestedTag}`));
  if (pkg?.revision) label.title = `已解析 commit：${pkg.revision}`;
  const edges = pkg?.dependencies || [];
  if (missing || cycle || !edges.length) {
    const leaf = textNode('div', 'package-leaf', ''); leaf.append(label); li.append(leaf); return li;
  }
  const branch = document.createElement('details'), summary = document.createElement('summary');
  summary.append(label); branch.append(summary); li.append(branch);
  const path = JSON.stringify([...ancestors, name]);
  let built = false;
  /**
   * 首次展开时创建当前节点子树，之后复用已有 DOM。
   */
  function buildChildren() {
    if (built) return; built = true;
    const children = textNode('ul', 'dependency-children', '');
    for (const edge of edges) children.append(packageNode(graph, edge.name, [...ancestors, name], edge.requested_tag, state));
    branch.append(children);
  }
  branch.ontoggle = () => {
    if (branch.open) { state.paths.add(path); buildChildren(); } else state.paths.delete(path);
  };
  if (state.paths.has(path)) { branch.open = true; buildChildren(); }
  return li;
}
/**
 * 更新工程 MTP 接入状态和依赖快照提示。
 */
function renderMtp(
  p // Unity 工程信息
) {
  const view = cards.get(p.instance_id); if (!view) return;
  const state = stateFor(p), data = state.data;
  const labels = {ready: 'MTP 已接入', absent: '未检测到 MTP', initializing: 'MTP 初始化中', incompatible: 'MTP 接口不兼容', error: 'MTP 读取失败'};
  if (data?.adapter === 'legacy') labels.ready = '旧版 MTP 已接入';
  if (data?.adapter === 'servicebase') labels.ready = '独立执行器已接入';
  view.status.textContent = !p.online ? '工程离线' : state.loading ? '正在读取包状态…' : state.error ? '包状态读取失败' : labels[data?.state] || '等待检查 MTP';
  if (p.online && !state.loading && !state.error && data?.mtp_version) view.status.textContent += ` · ${data.mtp_version}`;
  view.refresh.disabled = !p.online || state.loading;
  view.summary.textContent = data?.state === 'ready' ? `${data?.adapter==='servicebase'?'工程':'MTP'} 包依赖 · ${data.packages.length} 个包` : '工程包依赖';
  view.body.replaceChildren();
  if (state.error) view.body.append(textNode('p', 'tree-warning', state.error));
  if ((!p.online || state.stale) && data) view.body.append(textNode('p', 'tree-warning', '以下为服务器保存的历史快照，可能不是当前工程状态。'));
  if (!data) {
    view.body.append(textNode('p', 'tree-note', p.online ? '读取 MTP 依赖信息后，可在这里展开包节点。' : '等待工程重新连接后读取依赖。')); return;
  }
  view.body.append(textNode('p', 'tree-note', `${data.detail} · 最近采集 ${new Date(state.checkedAt || data.captured_at).toLocaleTimeString()}`));
  if (data.state !== 'ready') return;
  if (!data.packages.length) { view.body.append(textNode('p', 'tree-note', 'MTP 当前未记录任何包；若刚启动工程，请稍后刷新依赖。')); return; }
  try {
    const graph = indexPackages(dependencyPackages(data.packages)), tree = textNode('ul', 'dependency-tree', '');
    for (const name of graph.roots) {
      const root = packageNode(graph, name, [], '', state);
      if (graph.index.get(name).depth !== 0) root.append(textNode('span', 'tree-note inferred-root', '未归入已知根包'));
      tree.append(root);
    }
    view.body.append(tree);
  } catch (error) { view.body.append(textNode('p', 'tree-warning', requestError(error))); }
}
/**
 * 读取并缓存工程快照；强制刷新时先排队等待 Unity 重新采集。
 */
async function loadMtp(
  id, // 目标工程或任务 ID
  force = false // 是否先请求 Unity 重新采集快照
) {
  const p = projects.find(p => p.instance_id === id); if (!p) return;
  const state = stateFor(p); if (state.loading) return;
  state.loading = true; state.checked = true; state.error = ''; renderMtp(p);
  try {
    await limitedLoad(async () => {
      if (mtpStates.get(id) !== state) return;
      if (force && p.transport === 'pull') {
        const queued = await fetch(`/api/projects/${encodeURIComponent(id)}/mtp/refresh/`, {
          method:'POST', headers:csrfHeaders(), signal:AbortSignal.timeout(20000)
        });
        const task = await queued.json();
        if (!queued.ok) throw new Error(task.error || '刷新请求失败');
        if (task.task_id) await waitTask(task.task_id);
      }
      const res = await fetch(`/api/projects/${encodeURIComponent(id)}/mtp/`, {signal: AbortSignal.timeout(20000)});
      const result = await res.json();
      if (!res.ok) throw new Error(result.error || `HTTP ${res.status}`);
      const current = projects.find(p => p.instance_id === id);
      if (!current || mtpStates.get(id) !== state) return;
      state.data = result.response; state.stale = result.stale; state.checkedAt = result.checked_at;
      state.digest = p.snapshot_digest;
    });
  } catch (error) { state.error = requestError(error); }
  finally {
    state.loading = false;
    renderPackageWorkspace();
    const current = projects.find(p => p.instance_id === id);
    if (current && mtpStates.get(id) === state) renderMtp(current);
  }
}
/**
 * 刷新分页工程列表，丢弃在请求期间已被新操作取代的结果。
 */
async function refresh() {
  if (loading) return; loading = true;
  const revision = listRevision;
  try {
    const res = await fetch(`/api/projects/?page=${page}&page_size=50&q=${encodeURIComponent($('project-search').value)}`, {signal: AbortSignal.timeout(20000)});
    if (!res.ok) throw new Error(`服务返回 ${res.status}`);
    const listing = await res.json(), rows = listing.projects;
    if (revision !== listRevision) return;
    page = listing.page; pages = listing.pages; total = listing.total;
    projects = rows;
    if (!projects.some(p => p.instance_id === selectedId)) selectedId = projects.find(p => p.online)?.instance_id;
    render(); $('page-error').hidden = true;
    $('sync-status').textContent = `已同步 · ${new Date().toLocaleTimeString()}`;
  } catch (e) {
    $('page-error').textContent = `无法刷新工程列表：${requestError(e)}`; $('page-error').hidden = false;
    $('sync-status').textContent = '正在重连 · 保留上次状态';
  } finally { loading = false; }
}
$('refresh').onclick = refresh;
$('prev-page').onclick = () => { if (page > 1 && !loading) { page--; listRevision++; refresh(); } };
$('next-page').onclick = () => { if (page < pages && !loading) { page++; listRevision++; refresh(); } };
$('project-search').onchange = () => { page = 1; listRevision++; refresh(); };
$('message-form').onsubmit = async e => {
  e.preventDefault(); if (sending || !selectedId) return;
  const target = projects.find(p => p.instance_id === selectedId);
  if (!target?.online) return;
  sending = true; render(); $('send-error').hidden = true;
  const method = $('method').value;
  $('response-meta').textContent = `等待 ${target.project_name}…`;
  try {
    const res = await fetch(`/api/projects/${encodeURIComponent(target.instance_id)}/message/`, {
      method: 'POST', headers: {'Content-Type': 'application/json', 'X-CSRFToken': document.querySelector('[name=csrfmiddlewaretoken]').value},
      body: JSON.stringify({method, message: $('message').value}), signal: AbortSignal.timeout(20000)
    });
    let data = await res.json();
    if (!projects.some(p => p.instance_id === target.instance_id)) return;
    if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
    if (res.status === 202) {
      $('response-meta').textContent = `已排队，等待 ${target.project_name} 领取…`;
      data = await waitTask(data.task_id);
    }
    $('response-meta').textContent = `${method} · 已收到工程反馈`;
    $('response-output').textContent = JSON.stringify(data.response, null, 2);
  } catch (err) {
    $('send-error').textContent = requestError(err); $('send-error').hidden = false;
    $('response-meta').textContent = '请求失败'; $('response-output').textContent = requestError(err);
  } finally { sending = false; render(); }
};
refresh(); setInterval(() => { if (!document.hidden) refresh(); }, 5000);


/**
 * 渲染包列表或工程树及详情，保存按设备会话区分的标签选择。
 */
function renderPackageWorkspace() {
  const p = projects.find(p => p.instance_id === selectedId);
  const state = p ? stateFor(p) : null;
  const packages = state?.data?.packages || [];
  if (!packages.some(pkg => packageKey(pkg) === selectedPackage)) selectedPackage = packages.length ? packageKey(packages[0]) : null;
  const tagSelectionKey = JSON.stringify([selectedId, p?.session_id, selectedPackage]);
  const signature = JSON.stringify([selectedId, selectedPackage, selectedTags.get(tagSelectionKey), detailTab, packageTab, state?.data?.catalog_updated_at, state?.data?.captured_at, state?.digest, state?.loading, state?.error, p?.online, state?.stale]);
  if (signature === packageViewSignature) return;
  packageViewSignature = signature;
  $('package-heading').textContent = p ? `${p.project_name} · ${packageTab === 'tree' ? '工程依赖树' : '全部包列表'} (${packageTab === 'tree' ? (state?.data?.project_packages || []).length : packages.length})` : '工程包列表';
  $('refresh-packages').disabled = !p?.online || state?.loading;
  $('package-note').textContent = !p ? '请选择一个工程。' : state?.error || (state?.loading ? '正在读取包信息…' : state?.data?.detail || '等待工程上传包快照。');
  if (p && state?.data) $('package-note').textContent += ' ' + (state.data.catalog_detail || '');
  if (p && (!p.online || state?.stale)) $('package-note').textContent += ' 当前显示历史快照。';
  const assetsArea=$('assets-packages'); assetsArea.replaceChildren();
  const locals=state?.data?.assets_packages || [];
  if(locals.length){
    assetsArea.append(textNode('summary','',`Assets 本地包 (${locals.length})`));
    assetsArea.append(textNode('p','tree-note','依据 package.json 识别；不代表包内所有资源已完整核验。'));
    for(const item of locals){const row=textNode('p','','');row.append(textNode('strong','',item.display_name||item.name),textNode('span','',` · ${item.version||'版本未知'} · ${item.root}`));if(item.warning)row.append(textNode('small','tree-note',` · ${item.warning}`));assetsArea.append(row);}
  }
  assetsArea.hidden=!locals.length;
  const list = $('package-list'); list.replaceChildren();
  if (!packages.some(pkg => packageKey(pkg) === selectedPackage)) selectedPackage = packages.length ? packageKey(packages[0]) : null;
  $('package-columns').hidden = packageTab === 'tree';
  for (const [id, active] of [['tab-catalog',packageTab === 'catalog'],['tab-project-tree',packageTab === 'tree']]) {
    $(id).classList.toggle('active',active); $(id).setAttribute('aria-selected',String(active));
  }
  if (packageTab === 'tree') {
    const records = state?.data?.project_packages || [];
    const tree = textNode('ul','dependency-tree','');
    const graph = indexPackages(dependencyPackages(records));
    for (const name of graph.roots) tree.append(packageNode(graph,name,[], '',state));
    list.append(tree);
    if (!records.length) list.append(textNode('p','package-empty','工程暂无依赖记录。'));
  }
  for (const pkg of packageTab === 'catalog' ? packages : []) {
    const row = textNode('button', 'package-row' + (packageKey(pkg) === selectedPackage ? ' selected' : ''), '');
    row.type = 'button'; row.setAttribute('aria-pressed', String(packageKey(pkg) === selectedPackage));
    const name = textNode('span', 'package-row-name', '');
    name.append(textNode('strong', '', packageDisplayName(pkg)), textNode('small', '', pkg.name));
    const flags = packageFlags(pkg);
    row.append(name, textNode('span', flags.debug === 'Debug' ? 'flag debug' : 'flag', flags.debug),
      textNode('span', flags.installed === '已安装' ? 'flag installed' : 'flag', flags.installed), textNode('span', 'flag', flags.origin));
    row.onclick = () => { selectedPackage = packageKey(pkg); renderPackageWorkspace(); };
    list.append(row);
  }
  if (!packages.length && packageTab === 'catalog') list.append(textNode('p', 'package-empty', state?.data ? '此快照没有包记录。' : '尚无包快照，可点击刷新包信息。'));
  const detail = $('package-detail'); detail.replaceChildren();
  const record = packages.find(pkg => packageKey(pkg) === selectedPackage);
  if (!record) { detail.append(textNode('p', 'package-empty', '选择左侧包，查看信息和依赖。')); return; }
  const tags = packageTags(record);
  const selectedTag = tags.includes(selectedTags.get(tagSelectionKey)) ? selectedTags.get(tagSelectionKey) : record.tag || tags[0] || '';
  const pkg = packageAtTag(record,selectedTag);
  const title = textNode('div', 'package-detail-heading', '');
  title.append(textNode('div', 'eyebrow', 'PACKAGE INFORMATION'), textNode('h2', '', packageDisplayName(pkg)), textNode('p', 'package-name', pkg.name));
  detail.append(title);
  const actions = textNode('div','package-action-bar','');
  const actionNote = textNode('p','tree-note package-action-note','先预览依赖与附带插件，确认后自动执行。'); actionNote.setAttribute('role','status');
  const install = textNode('button','secondary',packageFlags(record).installed === '已安装' ? '预览更新' : '预览安装');install.type='button';
  install.disabled=!p?.online || !record.catalog_id || !(record.available_tags||[]).includes(selectedTag) || Boolean(state?.loading) || Boolean(record.assets_roots?.length);
  if(record.assets_roots?.length) actionNote.textContent='Assets 本地包仅提供识别与状态展示，保留原目录。';
  if(selectedTag && !(record.available_tags||[]).includes(selectedTag)) actionNote.textContent='当前显示已安装的 commit。请选择一个发布 Tag 来预览安装。';
  install.onclick=()=>openInstallationPlan({catalog_id:record.catalog_id,tag:selectedTag});
  actions.append(install);
  const rules=packageActions(record,selectedTag,Boolean(state?.loading));
  if(rules.showUninstall){const remove=textNode('button','secondary','卸载');remove.type='button';remove.disabled=!p?.online || Boolean(state?.loading);remove.onclick=()=>openInstallationPlan({operation:'uninstall',name:record.name});actions.append(remove);}
  detail.append(actions,actionNote);
  const tagField = textNode('div','package-tag-field','');
  const tagLabel = textNode('label','','Tag'); tagLabel.htmlFor = 'package-tag-select';
  const select = document.createElement('select'); select.id = 'package-tag-select';
  select.disabled = !tags.length;
  for (const tag of tags) {
    const option = textNode('option','',tag + (packageFlags(record).installed === '已安装' && tag === record.tag ? '（已安装）' : ''));
    option.value = tag; select.append(option);
  }
  if (!tags.length) select.append(textNode('option','','无可用 Tag'));
  select.value = selectedTag;
  select.onchange = () => { selectedTags.set(tagSelectionKey,select.value); renderPackageWorkspace(); document.getElementById('package-tag-select')?.focus(); };
  tagField.append(tagLabel,select); detail.append(tagField);
  if (selectedTag !== record.tag && !record.tag_metadata?.[selectedTag]) detail.append(textNode('p','tree-note','该 Tag 暂无元数据缓存。'));

  const tabs = textNode('div', 'detail-tabs', ''); tabs.setAttribute('role', 'tablist');
  for (const [key, label] of [['info','信息'],['dependencies','依赖'],['changelog','更新日志']]) {
    const button = textNode('button', detailTab === key ? 'active' : '', label);
    button.type = 'button'; button.setAttribute('role','tab'); button.setAttribute('aria-selected',String(detailTab === key));
    button.onclick = () => { detailTab = key; renderPackageWorkspace(); }; tabs.append(button);
  }
  detail.append(tabs);
  const content = textNode('div', 'package-detail-body', ''); content.setAttribute('role','tabpanel'); detail.append(content);
  if (detailTab === 'info') {
    const flags = packageFlags(pkg), values = [['当前安装 Tag',packageFlags(record).installed === '已安装' ? record.tag : '未安装'],['版本',pkg.version],['目录标识',pkg.catalog_id],['可用 Tag 数量',pkg.available_tags?.length?.toString()],['构建类型',flags.debug],['安装状态',flags.installed],
      ['本地目录',pkg.assets_roots?.join('、')],['安装判断依据',pkg.installation_source],['实际安装版本',pkg.installed_version],['包来源',flags.origin],['依赖深度',Number.isInteger(pkg.depth) && pkg.depth >= 0 ? String(pkg.depth) : '—'],['安装方式',pkg.is_local ? '本地包' : 'UPM'],
      ['作者',pkg.author],['Unity 要求',pkg.unity],['许可证',pkg.license],['Commit',pkg.revision]];
    const dl = document.createElement('dl');
    for (const [label,value] of values) dl.append(textNode('dt','',label), textNode('dd','',value || '未提供'));
    content.append(dl, textNode('h3','','说明'), textNode('p','package-description',pkg.description || '当前快照未提供说明。更新桥接脚本后可读取可用的包元数据。'));
  } else if (detailTab === 'changelog') content.append(textNode('p','package-description',pkg.changelog || '当前缓存未提供更新日志。'));
  else {
    const graph = indexPackages(dependencyPackages(packages,pkg)), tree = textNode('ul','dependency-tree','');
    tree.append(packageNode(graph,pkg.name,[], '',state)); content.append(tree);
    const parents = packages.filter(other => (other.dependencies || []).some(edge => edge.name === pkg.name));
    content.append(textNode('h3','','被以下包依赖'), textNode('p','package-description',parents.map(packageDisplayName).join('、') || '当前快照中没有其他包引用它。'));
  }
}
$('refresh-packages').onclick = () => { if (selectedId) loadMtp(selectedId, true); };
$('open-debug').onclick = () => $('debug-dialog').showModal();
$('close-debug').onclick = () => $('debug-dialog').close();

$('tab-catalog').onclick = () => { packageTab = 'catalog'; renderPackageWorkspace(); };
$('tab-project-tree').onclick = () => { packageTab = 'tree'; renderPackageWorkspace(); };
$('sync-catalog').onclick = async () => {
  const button = $('sync-catalog'); button.disabled = true; button.textContent = '正在同步…';
  try {
    const res = await fetch('/api/catalog/refresh/', {method:'POST',headers:csrfHeaders(),signal:AbortSignal.timeout(60000)});
    const result = await res.json();
    if (!res.ok) throw new Error(result.error || '目录同步失败');
    if (result.refreshing) throw new Error('目录正在由其他请求同步，请稍后再刷新包信息。');
    for (const state of mtpStates.values()) state.checked = false;
    if (selectedId) await loadMtp(selectedId);
  } catch (error) { $('package-note').textContent = requestError(error); }
  finally { button.disabled = false; button.textContent = '同步包信息'; }
};
