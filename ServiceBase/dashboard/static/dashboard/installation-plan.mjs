import {onProjectSelected} from './project-selection.mjs';
import {chooseZipDestination} from './zip-destination.mjs';
const el=(tag,text='',cls='')=>{const node=document.createElement(tag);node.textContent=text;node.className=cls;return node;};
let project=null,closeCurrent=()=>{};
onProjectSelected(value=>{if(project?.id!==value?.id||project?.session!==value?.session)closeCurrent();project=value;});
const headers=()=>({'Content-Type':'application/json','X-CSRFToken':document.querySelector('[name=csrfmiddlewaretoken]')?.value||''});
async function api(url,body){const response=await fetch(url,{method:body?'POST':'GET',headers:headers(),body:body?JSON.stringify(body):undefined,signal:AbortSignal.timeout(120000)});const data=await response.json();if(!response.ok)throw Error(data.error||'请求失败');return data;}
export function basicConflicts(rows){
  const seen=new Map(),messages=[];
  for(const row of rows.filter(r=>r.selected)){
    const signature=`${row.build_id}:${row.destination_root||''}`;
    if(seen.has(row.plugin_id)&&seen.get(row.plugin_id)!==signature)messages.push(`${row.name} 勾选了多个构建或目录，请留意文件和 GUID 冲突。`);
    seen.set(row.plugin_id,signature);
  }
  return [...new Set(messages)];
}
export const stepLabel=target=>({legacy_migrate:'迁移旧记录',plugin:'导入插件',plugin_update:'更新插件',plugin_remove:'删除旧插件',upm_remove:'卸载包',plan_verify:'最终核对',skip:'跳过'}[target.operation]||'安装 / 更新包');
export const planRows=plan=>[...plan.packages.flatMap(p=>p.plugins),...(plan.cleanup_plugins||[])].map(r=>({...r,selected:r.default_selected??r.available,destination_root:r.destination_root||'',replace_record:''}));
export const visiblePlanPackages=plan=>plan.packages.filter(p=>p.target.source==='git'||p.plugins.length>0);
export async function openInstallationPlan(root,existing=null){
  if(!project)return;
  closeCurrent();const target={...project};let closed=false,busy=false,selectionDigest='',revision=0;
  const base=`/api/projects/${encodeURIComponent(target.id)}/installation-plans/`;
  const dialog=el('dialog','','installation-plan-dialog');dialog.setAttribute('aria-labelledby','installation-plan-title');
  const heading=el('h2','安装计划预览');heading.id='installation-plan-title';
  const subtitle=el('p',`目标工程：${target.name}`),status=el('p','正在读取完整依赖及插件映射…'),body=el('div','','installation-plan-packages'),warnings=el('div','','plan-warnings'),sequence=el('div','','plan-sequence');
  status.setAttribute('role','status');warnings.setAttribute('role','status');
  const footer=el('div','','plan-actions'),close=el('button','关闭','secondary'),check=el('button','检查冲突和执行顺序','secondary'),confirm=el('button','确认并开始安装');
  for(const b of[close,check,confirm])b.type='button';check.disabled=confirm.disabled=true;
  footer.append(close,check,confirm);dialog.append(heading,subtitle,status,warnings,body,sequence,footer);document.body.append(dialog);
  const finish=()=>{closed=true;dialog.close();dialog.remove();if(closeCurrent===finish)closeCurrent=()=>{};};
  closeCurrent=finish;close.onclick=finish;dialog.oncancel=e=>{e.preventDefault();finish();};dialog.showModal();close.focus();
  try{
    const data=existing?{plan:existing}:await api(base,{root,request_key:crypto.randomUUID()});
    if(closed)return;
    const plan=data.plan;
    if(plan.state!=='ready'){status.textContent=plan.error||'预览尚未完成。若上次解析被中断，请关闭后重新生成。';return;}
    if(plan.job_id){status.textContent='该计划已经确认，进度可在安装任务列表查看。';return;}
    const action=plan.plan_action||'install';
    heading.textContent={install:'安装计划预览',update:'包更新预览',uninstall:'包卸载预览',plugin_update:'插件更新预览'}[action];
    confirm.textContent={install:'确认并开始安装',update:'确认更新',uninstall:'确认卸载',plugin_update:'确认更新插件'}[action];
    const rows=planRows(plan);
    const packages=visiblePlanPackages(plan),visibleNames=new Set(packages.map(p=>p.name));
    function warn(){
      warnings.replaceChildren();
      for(const text of [...plan.errors,...basicConflicts(rows)])warnings.append(el('p',text));
    }
    function changed(){revision++;selectionDigest='';confirm.disabled=true;sequence.replaceChildren();warn();}
    function choices(){return rows.map(({key,selected,destination_root,replace_record})=>({key,selected,destination_root,replace_record}));}
    status.textContent=`${packages.filter(p=>p.change==='add').length} 个新增 · ${packages.filter(p=>p.change==='change').length} 个变更 · ${packages.filter(p=>p.change==='unchanged').length} 个已满足的 MT Git 包`;
    if(packages.length<plan.packages.length)body.append(el('p','Unity 内置包和 Registry 依赖不在包列表展示，仍参与安装与最终核对。','plan-note'));
    for(const warning of plan.warnings)body.append(el('p',warning,'plan-note'));
    if(plan.removals?.length){
      const section=el('section','','plan-package');section.append(el('h3','将卸载的包'));
      for(const item of plan.removals)section.append(el('p',`${item.name} · ${item.version} · ${item.reason}`));body.append(section);
    }
    if(plan.retained_dependencies?.length){
      const section=el('details','','plan-package');section.append(el('summary','保留的旧依赖'));
      for(const item of plan.retained_dependencies)section.append(el('p',`${item.name} · ${item.reason}`));body.append(section);
    }
    if(plan.file_diff){
      status.textContent=`${plan.plugin_update.name} · ${plan.plugin_update.previous_build_id.slice(0,12)} → ${plan.plugin_update.build_id.slice(0,12)}`;
      body.append(el('p',`更新目录：${plan.plugin_update.destination_root||'资源原始目录'}`));
      for(const [key,label] of Object.entries({added:'新增文件',changed:'替换文件',removed:'待清理旧文件（修改或共用时保留）',unchanged:'复用或保留文件',guid_changes:'GUID 变化，请检查资源引用'})){
        const section=el('details','','plan-package');section.append(el('summary',`${label} · ${plan.file_diff[key].length}`));
        for(const path of plan.file_diff[key])section.append(el('p',path));body.append(section);
      }
    }
    if(action==='uninstall')status.textContent=`${plan.removals?.length||0} 个待卸载包 · ${plan.retained_dependencies?.length||0} 个保留依赖`;
    function pluginRow(offered){
      const item=rows.find(r=>r.key===offered.key),row=el('div','','plan-plugin'),label=el('label'),checkbox=document.createElement('input');checkbox.type='checkbox';checkbox.checked=item.selected;checkbox.disabled=!item.available;
      label.append(checkbox,el('span',`${item.action==='remove'?'删除旧插件 · ':''}${item.name} · ${item.version||'unversioned'} · ${item.format}${item.required?' · 包标记为必需':''}`));
      checkbox.onchange=()=>{item.selected=checkbox.checked;changed();};row.append(label);
      if(!item.available)row.append(el('p',item.reason));
      const chosen=el('span',item.destination_root||'尚未选择目录'),choose=el('button','选择 ZIP 根目录','secondary');choose.type='button';
      if(item.update_options?.length){
        const mode=el('select');mode.setAttribute('aria-label',`${item.name} 导入或替换版本`);
        const fresh=el('option','另行导入（保留旧记录）');fresh.value='';mode.append(fresh);
        for(const old of item.update_options){const option=el('option',`更新旧构建 ${old.build_id.slice(0,12)} · ${old.destination_root||'资源原始目录'}`);option.value=old.record_key;mode.append(option);}
        mode.onchange=()=>{item.replace_record=mode.value;const old=item.update_options.find(o=>o.record_key===mode.value);item.destination_root=old?.destination_root||'';chosen.textContent=item.destination_root||'尚未选择目录';choose.disabled=!!old||!item.available;changed();};row.append(mode);
      }
      if(item.action==='remove')row.append(el('p',`原目录：${item.destination_root||'资源原始目录'}；默认保留，勾选后在包处理完成后删除。`));
      else if(item.format==='zip'){
        choose.disabled=!item.available;
        choose.onclick=async()=>{choose.disabled=true;try{const value=await chooseZipDestination(target,item.name);if(!closed&&value!==null){item.destination_root=value;chosen.textContent=value;changed();}}catch(error){if(!closed)status.textContent=error.message;}finally{choose.disabled=!item.available||!!item.replace_record;}};
        row.append(choose,chosen);
      }
      return row;
    }
    for(const pkg of packages){
      const details=el('details','','plan-package');details.open=pkg.plugins.length>0;
      const label={add:'新增',change:'变更',unchanged:'已满足'}[pkg.change];
      details.append(el('summary',`${label} · ${pkg.display_name} · ${pkg.before?pkg.before+' → ':''}${pkg.version} · ${pkg.plugins.length} 个插件`),el('p',pkg.name),el('code',pkg.target.identifier));
      const dependencies=pkg.dependencies.filter(name=>visibleNames.has(name));
      if(dependencies.length)details.append(el('p','依赖：'+dependencies.join('、')));
      if(pkg.unity)details.append(el('p','Unity 要求：'+pkg.unity));
      for(const offered of pkg.plugins)details.append(pluginRow(offered));
      body.append(details);
    }
    if(plan.cleanup_plugins?.length){const section=el('details','','plan-package');section.open=true;section.append(el('summary','可选：清理不再关联的旧插件'));for(const offered of plan.cleanup_plugins)section.append(pluginRow(offered));body.append(section);}
    warn();check.disabled=plan.errors.length>0;
    check.onclick=async()=>{
      if(busy)return;
      if(rows.some(r=>r.selected&&r.action!=='remove'&&r.format==='zip'&&!r.destination_root)){status.textContent='请为已勾选的 ZIP 选择根目录。';return;}
      busy=true;check.disabled=true;confirm.disabled=true;const stamp=revision;
      status.textContent='正在核验所选制品并生成执行顺序…';
      try{
        const result=await api(`${base}${plan.id}/selection/`,{digest:plan.digest,plugins:choices()});
        if(closed||stamp!==revision)return;
        selectionDigest=result.selection_digest;sequence.replaceChildren(el('h3','确认后的执行顺序'));
        const list=el('ol');
        for(const step of result.steps)list.append(el('li',`${step.state==='skipped'?'跳过':stepLabel(step.target)} · ${step.target.name}${step.target.destination_root?' → '+step.target.destination_root:''}${step.target.reason?'（'+step.target.reason+'）':''}`));
        sequence.append(list);warn();for(const note of (result.warnings||[]).filter(note=>!plan.warnings.includes(note)))warnings.append(el('p',note));
        if(result.conflicts.length){warnings.append(el('strong','所选插件存在冲突（允许继续确认）'));for(const conflict of result.conflicts)warnings.append(el('p',conflict));}
        status.textContent=`共 ${result.steps.length} 步，按上述顺序执行。取消勾选的插件会显示为跳过。`;confirm.disabled=false;
      }catch(error){if(!closed)status.textContent=error.message;}
      finally{busy=false;check.disabled=plan.errors.length>0;}
    };
    confirm.onclick=async()=>{
      if(busy||!selectionDigest)return;busy=true;confirm.disabled=check.disabled=true;
      const controls=[...body.querySelectorAll('input,button,select')].map(node=>[node,node.disabled]);controls.forEach(([node])=>node.disabled=true);
      try{
        await api(`${base}${plan.id}/confirm/`,{digest:plan.digest,plugins:choices(),selection_digest:selectionDigest});
        if(!closed){finish();window.dispatchEvent(new Event('installation-created'));}
      }catch(error){if(!closed)status.textContent=error.message;}
      finally{busy=false;if(!closed){controls.forEach(([node,disabled])=>node.disabled=disabled);confirm.disabled=!selectionDigest;check.disabled=false;}}
    };
  }catch(error){if(!closed)status.textContent=error.message;}
}
export async function recentInstallationPlans(){
  if(!project)return;const target={...project};
  const data=await api(`/api/projects/${encodeURIComponent(target.id)}/installation-plans/`);
  if(project?.id!==target.id||project?.session!==target.session)return;
  const candidate=data.plans.find(p=>!p.job_id&&p.state==='ready');
  if(candidate)return openInstallationPlan(null,candidate);
  throw Error('没有尚未确认的预览，请在包信息面板生成安装计划。');
}
