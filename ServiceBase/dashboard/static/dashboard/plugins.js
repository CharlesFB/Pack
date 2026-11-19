import {requestError} from './request-error.mjs?v=plugin-lifecycle13';
import {openInstallationPlan} from './installation-plan.mjs?v=mt-packages12';
import {onProjectSelected} from './project-selection.mjs';
import {chooseZipDestination} from './zip-destination.mjs';
import {renderPluginContents} from './plugin-contents.mjs';
const $ = id => document.getElementById(id);
const node = (tag, text, cls = '') => { const e = document.createElement(tag); e.textContent = text; e.className = cls; return e; };
let initialized = false, selected = null, page = 1, total = 0, requestId = 0, detailId = 0, loadId = 0;
const pageSize = 10;
let project = null, selectedBuild = null, displayedPlugin = null, projectRevision = 0;
const importKeys = new Map();
const importBusy = new Set();
const importRenders = new WeakMap();
let disposeContents=()=>{};
onProjectSelected(value => {
  project=value; projectRevision++;
  const area=$('plugin-import-actions');
  if (area && displayedPlugin) { area.replaceChildren(); renderImport(area,displayedPlugin,displayedPlugin.builds[selectedBuild || 0]); }
});
async function get(url) {
  const response = await fetch(url, { signal: AbortSignal.timeout(15000) });
  const data = await response.json();
  if (!response.ok) throw new Error(data.error || `请求失败 (${response.status})`);
  return data;
}
function showError(message) { $('plugin-error').textContent = message; $('plugin-error').hidden = !message; }
function switchTab(plugins) {
  $('plugins-panel').hidden = !plugins; $('projects-panel').hidden = plugins;
  for (const [id, active] of [['workspace-projects', !plugins], ['workspace-plugins', plugins]]) {
    $(id).setAttribute('aria-selected', String(active)); $(id).tabIndex = active ? 0 : -1;
  }
  if (plugins && !initialized) load();
}
$('workspace-projects').onclick = () => { location.hash = 'projects'; switchTab(false); };
$('workspace-plugins').onclick = () => { location.hash = 'plugins'; switchTab(true); };
for (const id of ['workspace-projects', 'workspace-plugins']) $(id).onkeydown = event => {
  if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) {
    event.preventDefault(); const target = event.key === 'Home' ? 'workspace-projects' : event.key === 'End' ? 'workspace-plugins' : id === 'workspace-projects' ? 'workspace-plugins' : 'workspace-projects';
    $(target).click(); $(target).focus();
  }
};
window.addEventListener('hashchange', () => switchTab(location.hash === '#plugins'));
async function load() {
  const revision = ++loadId;
  initialized = true; showError(''); $('plugin-totals').textContent = '正在读取仓库…';
  try {
    const data = await get('/api/plugins/groups/');
    if (revision !== loadId) return;
    const value = $('plugin-group').value;
    $('plugin-group').replaceChildren(new Option('全部分类', ''));
    for (const group of data.groups) $('plugin-group').append(new Option(`${group.name} (${group.plugin_count})`, group.id));
    $('plugin-group').value = [...$('plugin-group').options].some(o => o.value === value) ? value : '';
    $('plugin-totals').textContent = `${data.groups.length} 个分类 · ${data.plugin_count} 个插件 · ${data.build_count} 个构建`;
    await loadList();
  } catch (error) { if (revision === loadId) { initialized = false; $('plugin-totals').textContent = '仓库读取失败'; showError(requestError(error)); } }
}
async function loadList() {
  disposeContents();
  const revision = ++requestId; ++detailId; showError('');
  $('plugin-list').replaceChildren(); $('plugin-detail').replaceChildren(node('p', '正在读取插件…'));
  $('plugin-prev').disabled = true; $('plugin-next').disabled = true;
  $('plugin-list-status').textContent = '正在读取插件…';
  try {
    const params = new URLSearchParams({ group: $('plugin-group').value, q: $('plugin-search').value, page, page_size: pageSize });
    const data = await get(`/api/plugins/?${params}`);
    if (revision !== requestId) return;
    total = data.total;
    $('plugin-list').replaceChildren();
    $('plugin-list-status').textContent = total ? `找到 ${total} 个插件` : '没有匹配的插件，请调整分类或搜索条件。';
    $('plugin-page').textContent = `${page} / ${Math.max(1, Math.ceil(total / pageSize))}`;
    $('plugin-prev').disabled = page <= 1; $('plugin-next').disabled = page * pageSize >= total;
    selected = data.plugins.some(p => p.id === selected) ? selected : data.plugins[0]?.id;
    for (const plugin of data.plugins) {
      const row = node('button', '', 'plugin-item'); row.type = 'button'; row.dataset.plugin = plugin.id;
      row.setAttribute('aria-pressed', String(plugin.id === selected));
      row.append(node('strong', plugin.name));
      row.append(node('small', `${plugin.group_name} · ${plugin.build_count} 个构建 · ${plugin.formats.join(' / ')}`));
      row.append(node('small', plugin.versions.map(v => v || 'unversioned').join(' · ')));
      if (plugin.review_count) row.append(node('small', `${plugin.review_count} 项导入前审查记录`));
      if (plugin.available_count !== plugin.build_count) row.append(node('small', '部分资源文件不可用'));
      row.onclick = () => { selected = plugin.id; selectRows(); loadDetail(); };
      $('plugin-list').append(row);
    }
    if (selected) await loadDetail();
    else $('plugin-detail').replaceChildren(node('p', '选择插件查看版本与详情。'));
  } catch (error) { if (revision === requestId) { showError(requestError(error)); $('plugin-list-status').textContent = '读取失败，请重试。'; $('plugin-detail').replaceChildren(node('p', '插件列表读取失败，请刷新仓库重试。')); } }
}
function selectRows() { document.querySelectorAll('.plugin-item').forEach(e => e.setAttribute('aria-pressed', String(e.dataset.plugin === selected))); }
async function loadDetail() {
  disposeContents();
  const revision = ++detailId;
  $('plugin-detail').replaceChildren(node('p', '正在读取版本详情…'));
  try {
    const data = await get(`/api/plugins/${encodeURIComponent(selected)}/`);
    if (revision !== detailId) return;
    renderDetail(data.plugin, 0);
  } catch (error) { if (revision === detailId) $('plugin-detail').replaceChildren(node('p', requestError(error))); }
}
function renderDetail(plugin, buildIndex) {
  disposeContents();
  displayedPlugin=plugin; selectedBuild=buildIndex;
  const area = $('plugin-detail'), build = plugin.builds[buildIndex]; area.replaceChildren();
  area.append(node('p', plugin.group_name, 'eyebrow'), node('h2', plugin.name));
  const label = node('label', '版本 / 构建'); label.htmlFor = 'plugin-build';
  const select = document.createElement('select'); select.id = 'plugin-build';
  plugin.builds.forEach((b, i) => select.append(new Option(`${b.version || 'unversioned'} · ${b.format} · ${b.id.slice(0, 8)}`, String(i))));
  select.value = String(buildIndex); select.onchange = () => { renderDetail(plugin, Number(select.value)); $('plugin-build').focus(); };
  area.append(label, select);
  const badges = node('div', '', 'plugin-badges');
  const availability = { available: '仓库文件可用', missing: '资源文件缺失', size_mismatch: '资源大小异常', metadata_unavailable: '元数据不可用' };
  badges.append(node('span', availability[build.availability], 'plugin-badge' + (build.availability === 'available' ? ' good' : '')),
    node('span', build.catalog_status === 'current' ? '当前索引' : '历史构建', 'plugin-badge'));
  if (!build.version) badges.append(node('span', '版本未确认', 'plugin-badge'));
  area.append(badges, node('p', build.description || '暂无描述', 'plugin-description'));
  const facts = node('dl', '', 'plugin-facts');
  for (const [name, value] of [['格式', build.format || '未确认'], ['Unity 要求', build.unity || '未提供'],
  ['发布时间', build.published_at || '未提供'], ['资源大小', build.size_bytes == null ? '未知' : `${(build.size_bytes / 1024 / 1024).toFixed(2)} MiB`],
  ['资源数量', String(build.asset_count)], ['SHA-256', build.sha256]]) facts.append(node('dt', name), node('dd', value));
  area.append(facts);
  const actionArea=node('div',''); actionArea.id='plugin-import-actions'; area.append(actionArea); renderImport(actionArea,plugin,build);
  const contents=node('section','','plugin-contents'); area.append(contents); disposeContents=renderPluginContents(contents,plugin,build);
  area.append(node('h3', '导入前审查'));
  area.append(node('p', '元数据与安装兼容性尚待确认；仓库可用不代表已通过导入验收。'));
  for (const item of build.reviews) {
    const details = node('details', '', 'plugin-review'); details.append(node('summary', `${item.label} (${item.count})`));
    const list = node('ul', ''); for (const sample of item.samples) list.append(node('li', sample));
    if (item.count > item.samples.length && item.samples.length) list.append(node('li', '仅展示前 8 条路径'));
    details.append(list); area.append(details);
  }
  area.append(node('h3', '来源与包关联'));
  for (const source of build.sources) area.append(node('div', `GitLab ${source.project_id} · ${source.ref}\n${source.filename}\ncommit ${source.commit}`, 'plugin-reference'));
  for (const ref of build.package_references) area.append(node('div', `${ref.name} · ${ref.version} · ${ref.current ? '当前包索引' : '历史包版本'}\n${ref.filename}\ncommit ${ref.commit}`, 'plugin-reference'));
  if (!build.sources.length && !build.package_references.length) area.append(node('p', '暂无来源关联记录。'));
}
$('plugin-group').onchange = () => { page = 1; loadList(); };
let timer; $('plugin-search').oninput = () => { clearTimeout(timer); timer = setTimeout(() => { page = 1; loadList(); }, 250); };
$('plugin-reload').onclick = () => { page = 1; load(); };
$('plugin-prev').onclick = () => { if (page > 1) { page--; loadList(); } };
$('plugin-next').onclick = () => { if (page * pageSize < total) { page++; loadList(); } };
switchTab(location.hash === '#plugins');

async function renderImport(area, plugin, build) {
  const stamp=Symbol(); importRenders.set(area,stamp);
  if (!project) { area.replaceChildren(node('p','请先选择目标工程。')); return; }
  const target=project, revision=projectRevision;
  const status=node('p',`目标工程：${target.name} · 正在读取导入记录…`); if (!area.children.length) area.append(status);
  try {
    const [records,jobs]=await Promise.all([get(`/api/projects/${encodeURIComponent(target.id)}/plugins/`), get(`/api/projects/${encodeURIComponent(target.id)}/installations/`)]);
    if (revision!==projectRevision || !area.isConnected || importRenders.get(area)!==stamp) return;
    const signature=JSON.stringify([revision,plugin.id,build.id,records,jobs,[...importBusy]]);
    if (area.dataset.signature===signature && area.children.length>1) return;
    area.dataset.signature=signature; area.replaceChildren(status);
    const saved=records.installations.filter(r=>r.plugin_id===plugin.id && r.build_id===build.id);
    const live=records.fresh && !records.scanning;
    status.textContent=`${target.name} · ${records.error || (records.scanning ? '等待 Unity 核对插件状态' : records.fresh ? 'Unity 已同步导入记录' : '状态未更新，请连接 Unity 后核对')}`;
    if (records.scanned_at) area.append(node('small',`核对时间：${new Date(records.scanned_at).toLocaleString()}`));
    const refresh=node('button','核对工程插件状态','secondary'); refresh.type='button';
    refresh.disabled=!jobs.plugin_inventory_ready || records.scanning;
    refresh.onclick=async()=>{
      refresh.disabled=true;
      try {
        const response=await fetch(`/api/projects/${encodeURIComponent(target.id)}/plugins/refresh/`,{method:'POST',signal:AbortSignal.timeout(15000),headers:{'X-CSRFToken':document.querySelector('[name=csrfmiddlewaretoken]').value}});
        const data=await response.json(); if (!response.ok) throw new Error(data.error || '核对请求失败');
        if (revision===projectRevision) status.textContent='已请求 Unity 核对，完成后自动更新。';
      } catch(e) { if (revision===projectRevision) showError(requestError(e)); }
      finally { refresh.disabled=false; }
    };
    area.append(refresh);
    if (!saved.length) area.append(node('p',live ? '当前构建无 ServiceBase 管理的导入记录。' : '尚无当前构建的可用状态记录。'));
    const stateNames={recorded:'已保存导入记录',installed:'已导入 · 资源完整',modified:'已导入 · 资源有修改',missing:'记录中的资源已缺失',partial:'已导入 · 部分资源缺失',verified_at_import:'历史导入记录 · 等待 Unity 核对'};
    const running=jobs.jobs.some(j=>j.active);
    for (const record of saved) {
      const row=node('section','','plugin-project-record');
      row.append(node('strong',stateNames[record.status] || '状态未知'),node('p',`${record.destination_root || '资源原始目录'} · ${record.file_count} 项资源`));
      if (record.import_result && !['completed','reloaded'].includes(record.import_result)) {
        const reasons={failed:'导入失败',cancelled:'导入取消',persistence_failed:'导入回调未保存',compile_error:'插件编译报错',unknown:'旧任务缺少完成回调'};
        row.append(node('p','该次导入已跳过或未完成：'+(reasons[record.import_result] || '未确认完成')));
      }
      if (record.status!=='recorded' && record.matched!=null) row.append(node('p',`完整 ${record.matched} · 修改 ${record.modified} · 缺失 ${record.missing}`));
      if (record.roots?.length) row.append(node('small',`根目录：${record.roots.join('、')}`));
      if (record.samples?.length) row.append(node('p',`变化路径：${record.samples.join('、')}`));
      const remove=node('button','删除此导入记录及可清理文件','secondary'); remove.type='button';
      const identity=`${target.id}:${target.session}:remove:${record.record_key}:${record.record_digest}`;
      remove.disabled=!live || !jobs.plugin_management_ready || running || !record.record_key || importBusy.has(identity);
      remove.onclick=async()=>{
        if (importBusy.has(identity)) return;
        importBusy.add(identity); remove.disabled=true;
        try {
          if (!await confirmRemoval(target,plugin,build,record)) return;
          if (project?.id!==target.id || project?.session!==target.session) return;
          if (!importKeys.has(identity)) importKeys.set(identity,crypto.randomUUID());
          const response=await fetch(`/api/projects/${encodeURIComponent(target.id)}/installations/`,{method:'POST',signal:AbortSignal.timeout(15000),
            headers:{'Content-Type':'application/json','X-CSRFToken':document.querySelector('[name=csrfmiddlewaretoken]').value},
            body:JSON.stringify({operation:'plugin_remove',record_key:record.record_key,record_digest:record.record_digest,request_key:importKeys.get(identity)})});
          const data=await response.json(); if (!response.ok) throw new Error(data.error || '删除请求失败');
          importKeys.delete(identity);
          if (revision===projectRevision) window.dispatchEvent(new Event('installation-created'));
        } catch(e) { if (revision===projectRevision) showError(requestError(e)); }
        finally { importBusy.delete(identity); window.dispatchEvent(new Event('installation-status-changed')); }
      };
      row.append(remove); area.append(row);
    }
    for(const old of records.installations.filter(r=>r.plugin_id===plugin.id&&r.build_id!==build.id)){
      const row=node('section','','plugin-project-record');row.append(node('p',`旧构建 ${old.build_id.slice(0,12)} · ${old.destination_root||'资源原始目录'}`));
      const update=node('button','预览更新到所选版本','secondary');update.type='button';update.disabled=!live||!jobs.maintenance_executor_ready||running||!build.import_available;
      update.onclick=()=>openInstallationPlan({operation:'plugin_update',plugin_id:plugin.id,build_id:build.id,record_key:old.record_key,record_digest:old.record_digest});row.append(update);area.append(row);
      if(!jobs.maintenance_executor_ready)row.append(node('small','插件更新需要连接 v8 独立执行器。'));
    }
    if (saved.length && !jobs.plugin_management_ready) area.append(node('p',jobs.executor_diagnostic || '状态核对与删除需要版本 6 的独立执行器。'));
    const removal=records.removals?.find(r=>r.plugin_id===plugin.id && r.build_id===build.id);
    if (removal) {
      area.append(node('p',`最近删除：${new Date(removal.removed_at).toLocaleString()} · 移出 ${removal.removed} 项 · 保留 ${removal.retained} 项`));
      if (removal.retained_paths.length) area.append(node('small',`保留路径：${removal.retained_paths.join('、')}`));
    }
    const updateResult=records.updates?.find(r=>r.plugin_id===plugin.id&&r.build_id===build.id);
    if(updateResult){area.append(node('p',`最近更新：${new Date(updateResult.updated_at).toLocaleString()} · 清理旧文件 ${updateResult.removed} 项 · 保留 ${updateResult.retained} 项`));if(updateResult.retained_paths.length)area.append(node('small','保留路径：'+updateResult.retained_paths.join('、')));}
    if (build.format==='zip') area.append(node('p','导入前在网页选择工程 Assets 内的解压根目录。保留 ZIP 原有结构，确认后自动执行。'));
    const active=jobs.jobs.find(j=>j.active);
    const operationKey=`${target.id}:${target.session}:${plugin.id}:${build.id}`;
    const reason=importBusy.has(operationKey) ? '正在准备导入…' : build.import_reason || (build.format==='zip' && !jobs.zip_executor_ready ? jobs.executor_diagnostic || '请连接版本 5 的独立执行器以在网页选择 ZIP 目录' : !jobs.plugin_executor_ready ? jobs.executor_diagnostic || '请连接版本 3 的独立执行器' : active ? '工程有未结束任务，请先核对' : '');
    if (reason) area.append(node('p',reason));
    const button=node('button',`导入 ${build.format} 到 ${target.name}`); button.type='button'; button.disabled=!!reason; area.append(button);
    button.onclick=async()=>{
      if (importBusy.has(operationKey)) return;
      importBusy.add(operationKey); button.disabled=true; showError('');
      try {
        const destination=build.format==='zip' ? await chooseZipDestination(target,plugin.name) : '';
        if (destination===null || project?.id!==target.id || project?.session!==target.session) return;
        const identity=`${operationKey}:${destination}`;
        if (!importKeys.has(identity)) importKeys.set(identity,crypto.randomUUID());
        const response=await fetch(`/api/projects/${encodeURIComponent(target.id)}/installations/`,{method:'POST',signal:AbortSignal.timeout(120000),
          headers:{'Content-Type':'application/json','X-CSRFToken':document.querySelector('[name=csrfmiddlewaretoken]').value},
          body:JSON.stringify({operation:'plugin',plugin_id:plugin.id,build_id:build.id,destination_root:destination,request_key:importKeys.get(identity)})});
        const result=await response.json(); if (!response.ok) throw new Error(result.error || '导入请求失败');
        importKeys.delete(identity);
        if (revision===projectRevision) { window.dispatchEvent(new Event('installation-created')); status.textContent='导入任务已建立，进度见上方任务列表。'; }
      } catch (error) { if (revision===projectRevision) showError(requestError(error)); }
      finally { importBusy.delete(operationKey); window.dispatchEvent(new Event('installation-status-changed')); }
    };
  } catch(error) { if (revision===projectRevision && area.isConnected && importRenders.get(area)===stamp) area.replaceChildren(node('p','工程插件状态读取失败：'+requestError(error)+'；连接恢复后自动重试。')); }
}

window.addEventListener('installation-status-changed', () => { const area=$('plugin-import-actions'); if (area && displayedPlugin) { area.replaceChildren(); renderImport(area,displayedPlugin,displayedPlugin.builds[selectedBuild || 0]); } });

function confirmRemoval(target,plugin,build,record) {
  return new Promise(resolve=>{
    const dialog=node('dialog','','plugin-remove-dialog');
    dialog.setAttribute('aria-labelledby','plugin-remove-title');
    const heading=node('h2','删除工程插件'); heading.id='plugin-remove-title';
    dialog.append(heading,node('p',`${target.name} · ${plugin.name} · ${build.version || 'unversioned'}`),
      node('p',`导入目录：${record.destination_root || record.roots?.join('、') || '资源原始目录'}`),
      node('p','按原归档清单移出未被其他包占用的文件（包括内容已变化的文件），同时删除对应 meta 和空父目录。额外生成文件与共享资源保留；移出文件备份在工程 .servicebase/plugins/trash。'));
    const cancel=node('button','取消','secondary'), confirm=node('button','确认删除'); cancel.type=confirm.type='button';
    dialog.append(cancel,confirm); document.body.append(dialog);
    let stop=()=>{},done=false;
    const finish=value=>{ if(done)return;done=true;stop();dialog.close();dialog.remove();resolve(value); };
    cancel.onclick=()=>finish(false); confirm.onclick=()=>finish(true); dialog.oncancel=e=>{e.preventDefault();finish(false);};
    stop=onProjectSelected(value=>{if(value?.id!==target.id || value?.session!==target.session)finish(false);});
    if (!done) { dialog.showModal(); cancel.focus(); }
  });
}
setInterval(()=>{
  const area=$('plugin-import-actions');
  if (area && displayedPlugin && !document.hidden && location.hash==='#plugins' && !document.querySelector('.plugin-remove-dialog')) {
    renderImport(area,displayedPlugin,displayedPlugin.builds[selectedBuild || 0]);
  }
},10000);
