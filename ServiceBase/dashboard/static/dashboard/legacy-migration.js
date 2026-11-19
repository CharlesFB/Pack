import {onProjectSelected} from './project-selection.mjs';
const $=id=>document.getElementById(id);
const node=(tag,value)=>{const n=document.createElement(tag);n.textContent=value;return n;};
const labels={plugin:'插件记录',root:'根包记录',local:'本地包记录',dependency:'依赖记录'};
const statuses={recorded:'历史记录已保存',baseline_matches:'与迁移时基线一致',modified:'资源已有修改',partial:'资源缺失或 GUID 异常'};
let project=null,generation=0,fetching=false,busy=false,ready=false,signature='',scanKey=null;
const selections=new Map();
function error(value){$('legacy-error').textContent=value;$('legacy-error').hidden=!value;}
function controls(){$('legacy-open').disabled=!project;$('legacy-scan').disabled=!project||!ready||busy;}
function headers(){return {'Content-Type':'application/json','X-CSRFToken':document.querySelector('[name=csrfmiddlewaretoken]')?.value||''};}
async function api(url,body){const response=await fetch(url,body?{method:'POST',headers:headers(),body:JSON.stringify(body),signal:AbortSignal.timeout(15000)}:{signal:AbortSignal.timeout(15000)});const data=await response.json();if(!response.ok)throw new Error(data.error||`请求失败 (${response.status})`);return data;}
async function refresh(){
  if(!project||fetching||!$('legacy-panel').open)return;
  fetching=true;const version=generation,base=`/api/projects/${encodeURIComponent(project.id)}/`;
  try{
    const [data,inventory]=await Promise.all([api(base+'legacy-migrations/'),api(base+'plugins/')]);
    if(version!==generation)return;
    ready=data.ready;controls();
    $('legacy-status').textContent=`目标工程：${project.name} · ${ready?'可以读取旧记录':'等待 v9 独立执行器连接'}`;
    const next=JSON.stringify([project.id,ready,data.previews,inventory.legacy_installations,inventory.fresh,inventory.scanning]);
    if(signature===next)return;signature=next;
    const area=$('legacy-previews');area.replaceChildren();
    for(const row of data.previews){
      const section=node('section','');section.className='legacy-preview-card';
      section.append(node('h3',`迁移预览 · ${new Date(row.created_at).toLocaleString()}`));
      if(row.job_id){section.append(node('p',row.state==='completed'?'所选记录已迁移。':'已提交迁移，请查看上方任务进度。'));area.append(section);continue;}
      if(row.state==='pending'){section.append(node('p',row.waiting_too_long?'等待 Unity 读取超过 90 秒；可检查连接后重新读取，尚未提交迁移。':'等待 Unity 读取旧记录…'));area.append(section);continue;}
      if(row.state!=='preview'||row.expired){section.append(node('p',row.preview.error||(row.expired?'预览已过期，请重新读取。':'已被新的读取请求替代。')));area.append(section);continue;}
      if(!selections.has(row.id))selections.set(row.id,new Set());
      const selected=selections.get(row.id);
      if(!row.preview.entries.length)section.append(node('p','没有找到支持迁移的旧记录。'));
      const confirm=node('button','迁移所选记录');confirm.type='button';confirm.className='secondary';confirm.disabled=!selected.size||busy||!ready;
      for(const item of row.preview.entries){
        const label=node('label','');label.className='legacy-choice';
        const check=document.createElement('input');check.type='checkbox';check.checked=selected.has(item.id);check.disabled=item.migrated||busy;
        check.onchange=()=>{if(check.checked)selected.add(item.id);else selected.delete(item.id);confirm.disabled=!selected.size||busy||!ready;};
        const detail=node('span',`${labels[item.kind]} · ${item.name}${item.migrated?' · 已迁移':''}`);
        detail.append(node('small',`${item.source_file}${item.tag?' · '+item.tag:''}${item.kind==='plugin'?` · ${item.file_count} 项资源，异常 ${item.missing} 项`:''}`),node('small',item.warning));
        label.append(check,detail);section.append(label);
      }
      confirm.onclick=async()=>{
        if(version!==generation||busy)return;busy=true;controls();confirm.disabled=true;error('');
        try{await api(`${base}legacy-migrations/${row.id}/confirm/`,{digest:row.digest,entries:[...selected].sort()});if(version===generation){window.dispatchEvent(new Event('installation-created'));signature='';}}
        catch(e){if(version===generation)error(e.message);}
        finally{busy=false;controls();if(version===generation){signature='';await refresh();}}
      };
      section.append(confirm);area.append(section);
    }
    const migrated=$('legacy-inventory');migrated.replaceChildren();
    const records=inventory.legacy_installations||[];
    if(records.length){
      migrated.append(node('h3',`已迁移记录 (${records.length})`),node('p',inventory.fresh?'Unity 已核对当前资源状态。':'当前为历史状态，等待 Unity 核对。'));
      for(const item of records)migrated.append(node('p',`${labels[item.kind]} · ${item.name} · ${statuses[item.status]||item.status}${item.kind==='plugin'?` · 一致 ${item.matched} / 修改 ${item.modified} / 异常 ${item.missing}`:''}`));
    }
  }catch(e){if(version===generation){ready=false;controls();error(e.message);}}
  finally{fetching=false;if(version!==generation)refresh();}
}
$('legacy-scan').onclick=async()=>{
  if(!project||!ready||busy)return;
  busy=true;controls();error('');const version=generation,base=`/api/projects/${encodeURIComponent(project.id)}/legacy-migrations/`;
  scanKey??=crypto.randomUUID();
  try{await api(base,{request_key:scanKey});if(version===generation){scanKey=null;signature='';}}
  catch(e){if(version===generation)error(e.message);}
  finally{busy=false;controls();if(version===generation)refresh();}
};
onProjectSelected(value=>{
  if(project?.id===value?.id&&project?.session===value?.session){project=value;controls();return;}
  project=value;generation++;ready=false;signature='';scanKey=null;selections.clear();
  $('legacy-panel').close();controls();error('');$('legacy-previews').replaceChildren();$('legacy-inventory').replaceChildren();
  $('legacy-status').textContent=project?'正在读取迁移状态…':'请先选择工程。';
});
$('legacy-open').onclick=()=>{if(!project)return;$('legacy-panel').showModal();$('legacy-panel').scrollTop=0;refresh();};
$('legacy-close').onclick=()=>$('legacy-panel').close();
setInterval(()=>{if($('legacy-panel').open)refresh();},5000);
