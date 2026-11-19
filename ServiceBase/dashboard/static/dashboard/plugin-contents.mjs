const node = (tag, text='', cls='') => { const e=document.createElement(tag); e.textContent=text; e.className=cls; return e; };
const readable = value => value.replace(/[\u0000-\u001f\u007f]/g, c => c==='\n' ? '\\n' : c==='\r' ? '\\r' : c==='\t' ? '\\t' : `\\u${c.charCodeAt(0).toString(16).padStart(4,'0')}`);
function size(bytes) {
  if (bytes==null) return '—';
  if (bytes<1024) return `${bytes} B`;
  if (bytes<1024*1024) return `${(bytes/1024).toFixed(1)} KB`;
  return `${(bytes/1024/1024).toFixed(1)} MB`;
}
function icon(folder) {
  const svg=document.createElementNS('http://www.w3.org/2000/svg','svg');
  svg.setAttribute('viewBox','0 0 24 24'); svg.setAttribute('aria-hidden','true');
  svg.setAttribute('class',`plugin-content-icon ${folder ? 'folder' : 'file'}`);
  const path=document.createElementNS(svg.namespaceURI,'path');
  path.setAttribute('d',folder ? 'M3 6h7l2 2h9v12H3z M3 6V4h7l2 2h9v2' : 'M5 3h9l5 5v13H5z M14 3v6h5 M8 13h8 M8 17h6');
  svg.append(path); return svg;
}

export function renderPluginContents(area, plugin, build) {
  let generation=0, disposed=false;
  const pending=new Set(), expanded=new Set();
  const title=node('h3','插件内容'), toolbar=node('div','','plugin-content-toolbar');
  const count=node('span','正在读取文件索引…'); count.setAttribute('role','status');
  const label=node('label'), meta=node('input'); meta.type='checkbox'; label.append(meta,document.createTextNode('显示 .meta'));
  const collapse=node('button','全部折叠','secondary'); collapse.type='button';
  toolbar.append(label,collapse);
  const viewport=node('div','','plugin-content-viewport');
  viewport.setAttribute('aria-label','插件内容目录'); viewport.setAttribute('role','region');
  const rootRow=node('div','','plugin-content-root'); rootRow.append(icon(true),node('strong',`${plugin.name}.${build.format}`));
  const tree=node('ul','','plugin-content-tree'); viewport.append(rootRow,tree);
  const hint=node('p',build.format==='zip' ? '压缩包内的原始路径；导入时放到所选根目录下。' : '按 Unity 资源路径展示，展开文件夹查看内容。','plugin-content-hint');
  area.append(title,hint,toolbar,viewport,count); count.className='plugin-content-count';
  const endpoint=`/api/plugins/${encodeURIComponent(plugin.id)}/builds/${encodeURIComponent(build.id)}/contents/`;
  function stopRequests() { for (const request of pending) request.abort(); pending.clear(); }
  async function load(list, path, page=1, trigger=null) {
    const revision=generation, controller=new AbortController(); pending.add(controller);
    const alive=()=>!disposed && generation===revision && list.isConnected;
    const loading=node('li','正在读取…','plugin-content-message'); loading.setAttribute('role','status');
    if (trigger) trigger.disabled=true;
    list.append(loading);
    try {
      const query=new URLSearchParams({version:build.version || '',path,page,meta:meta.checked ? '1' : '0'});
      const response=await fetch(`${endpoint}?${query}`,{signal:AbortSignal.any([controller.signal,AbortSignal.timeout(15000)])});
      const data=await response.json();
      if (!response.ok) throw new Error(data.error || '文件索引读取失败');
      if (!alive()) return;
      loading.remove(); trigger?.closest('li').remove();
      if (!path) count.textContent=`${data.folder_count} 个文件夹 · ${data.file_count} 个文件${meta.checked ? '（含 .meta）' : ''}`;
      for (const entry of data.entries) {
        const li=node('li');
        if (entry.folder) {
          const folder=node('details'), summary=node('summary','','plugin-content-row');
          const arrow=node('span','▸','plugin-content-arrow'); arrow.setAttribute('aria-hidden','true');
          summary.title=readable(entry.path); summary.append(arrow,icon(true),node('span',readable(entry.name),'plugin-content-name'),node('small','文件夹','plugin-content-size'));
          const children=node('ul','','plugin-content-tree'); folder.append(summary,children); li.append(folder);
          let loaded=false;
          folder.ontoggle=()=>{
            if (!alive()) return;
            if (folder.open) { expanded.add(entry.path); if (!loaded) { loaded=true; load(children,entry.path); } }
            else expanded.delete(entry.path);
          };
          if (expanded.has(entry.path)) folder.open=true;
        } else {
          const row=node('div','','plugin-content-row'); row.title=readable(entry.path);
          row.append(node('span','','plugin-content-arrow'),icon(false),node('span',readable(entry.name),'plugin-content-name'),node('small',size(entry.size_bytes),'plugin-content-size'));
          if (entry.meta) row.classList.add('plugin-content-meta');
          li.append(row);
        }
        list.append(li);
      }
      if (!data.total) list.append(node('li','此目录暂无可显示的文件。','plugin-content-message'));
      if (page*data.page_size<data.total) {
        const more=node('button',`加载更多（已显示 ${page*data.page_size} / ${data.total}）`,'secondary'); more.type='button';
        more.onclick=()=>load(list,path,page+1,more);
        const row=node('li','','plugin-content-message'); row.append(more); list.append(row);
      }
    } catch (error) {
      if (!alive() || controller.signal.aborted) return;
      loading.textContent=error.name==='TimeoutError' ? '读取超时，请重试。' : error.message;
      if (!path) count.textContent='文件索引读取失败';
      const retry=node('button','重试','secondary'); retry.type='button';
      retry.onclick=()=>{ loading.remove(); load(list,path,page,trigger); }; loading.append(retry);
      // The retry button owns this page; avoid two concurrent requests for the same rows.
    } finally { pending.delete(controller); }
  }
  meta.onchange=()=>{ generation++; stopRequests(); tree.replaceChildren(); count.textContent='正在读取文件索引…'; load(tree,''); };
  collapse.onclick=()=>{ expanded.clear(); for (const folder of tree.querySelectorAll('details[open]')) folder.open=false; };
  load(tree,'');
  return ()=>{ disposed=true; stopRequests(); };
}
