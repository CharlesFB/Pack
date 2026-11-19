import {onProjectSelected} from './project-selection.mjs';
const node = (tag, text) => { const e=document.createElement(tag); e.textContent=text; return e; };

// The dialog lives outside the polling detail panel so progress refreshes cannot erase a choice.
export function chooseZipDestination(project, pluginName) {
  return new Promise(resolve => {
    const dialog=node('dialog',''); dialog.className='zip-destination-dialog';
    dialog.setAttribute('aria-labelledby','zip-destination-title');
    const title=node('h2','选择 ZIP 解压根目录'); title.id='zip-destination-title';
    const description=node('p',`${project.name} · ${pluginName}。保留 ZIP 内原有目录结构，确认后自动导入。`);
    const label=node('label','工程目录'); label.htmlFor='zip-directory-path';
    const path=node('input',''); path.id='zip-directory-path'; path.value='Assets'; path.maxLength=512;
    const browse=node('button','读取目录'), up=node('button','上一级'), cancel=node('button','取消'), confirm=node('button','导入到此目录');
    const status=node('p',''), folders=node('div',''), actions=node('div','');
    status.setAttribute('role','status'); folders.className='zip-directory-list'; actions.className='zip-directory-actions';
    actions.append(up,browse,cancel,confirm); dialog.append(title,description,label,path,status,folders,actions);
    let revision=0, selected='', closed=false, controller=null, unsubscribe=()=>{};
    const finish=value=>{ if (closed) return; closed=true; controller?.abort(); unsubscribe(); dialog.close(); dialog.remove(); resolve(value); };
    cancel.onclick=()=>finish(null); dialog.oncancel=event=>{ event.preventDefault(); finish(null); };
    confirm.onclick=()=>{ if (selected) finish(selected); };
    path.oninput=()=>{ selected=''; confirm.disabled=true; };
    path.onkeydown=event=>{ if (event.key==='Enter') { event.preventDefault(); read(path.value); } };
    browse.onclick=()=>read(path.value);
    up.onclick=()=>read(path.value.includes('/') ? path.value.slice(0,path.value.lastIndexOf('/')) : 'Assets');
    const endpoint=`/api/projects/${encodeURIComponent(project.id)}/directories/`;
    async function read(value) {
      const current=++revision; controller?.abort(); controller=new AbortController();
      const signal=AbortSignal.any([controller.signal,AbortSignal.timeout(45000)]);
      selected=''; path.value=value; confirm.disabled=true; up.disabled=value==='Assets'; folders.replaceChildren(); status.textContent='正在读取工程目录…';
      async function request(url, options={}) {
        const response=await fetch(url,{...options,signal}); const data=await response.json();
        if (!response.ok) throw new Error(data.error || '目录读取失败');
        return data;
      }
      try {
        let data=await request(endpoint,{method:'POST',headers:{'Content-Type':'application/json','X-CSRFToken':document.querySelector('[name=csrfmiddlewaretoken]')?.value || ''},body:JSON.stringify({path:value})});
        while (data.state==='pending') {
          await new Promise(resolve=>setTimeout(resolve,1000));
          if (closed || current!==revision) return;
          data=await request(`${endpoint}?request_id=${encodeURIComponent(data.request_id)}`);
        }
        if (closed || current!==revision) return;
        if (data.state!=='completed') throw new Error(data.error || '目录读取失败');
        // Text may have changed while the device was reading; only the returned path is selectable.
        if (path.value!==value) { status.textContent='目录已修改，请点击“读取目录”确认。'; return; }
        selected=data.path; confirm.disabled=false;
        status.textContent=`解压到：${selected}/${data.truncated ? '（目录较多，仅显示部分，可输入完整路径继续读取）' : ''}`;
        for (const folder of data.folders) {
          const button=node('button',folder.slice(folder.lastIndexOf('/')+1)+'/'); button.type='button';
          button.onclick=()=>read(folder); folders.append(button);
        }
        if (!data.folders.length) folders.append(node('p','此目录下没有可浏览的子目录。'));
      } catch (error) {
        if (!closed && current===revision) status.textContent=signal.aborted ? '目录读取超时或已取消，请检查工程连接后重新读取。' : error.message;
      }
    }
    document.body.append(dialog); dialog.showModal();
    unsubscribe=onProjectSelected(value=>{ if (!value || value.id!==project.id || value.session!==project.session) finish(null); });
    if (closed) unsubscribe(); else read('Assets');
  });
}
