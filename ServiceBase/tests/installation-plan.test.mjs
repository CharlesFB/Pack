import test from 'node:test';
import assert from 'node:assert/strict';
import {basicConflicts,planRows,stepLabel,visiblePlanPackages} from '../dashboard/static/dashboard/installation-plan.mjs';
test('same plugin build and root deduplicate without warning, unchecked items ignored',()=>{
  const row={name:'ZIP',plugin_id:'zip',build_id:'a',destination_root:'Assets',selected:true};
  assert.deepEqual(basicConflicts([row,{...row},{...row,build_id:'b',selected:false}]),[]);
});
test('other versions and roots warn without mutating selection',()=>{
  const rows=[{name:'ZIP',plugin_id:'zip',build_id:'a',destination_root:'Assets',selected:true},{name:'ZIP',plugin_id:'zip',build_id:'b',destination_root:'Assets',selected:true}];
  assert.equal(basicConflicts(rows).length,1);assert.ok(rows.every(r=>r.selected));
  rows[1].build_id='a';rows[1].destination_root='Assets/Plugins';assert.equal(basicConflicts(rows).length,1);
});

test('old plugin cleanup is opt-in while available new plugins are selected',()=>{
  const rows=planRows({packages:[{plugins:[{key:'new',available:true}]}],cleanup_plugins:[{key:'old',available:true,action:'remove',default_selected:false,destination_root:'Assets/Old'}]});
  assert.equal(rows[0].selected,true);assert.equal(rows[1].selected,false);assert.equal(rows[1].destination_root,'Assets/Old');
});
test('maintenance sequence distinguishes removal and replacement',()=>{
  assert.equal(stepLabel({operation:'plugin_update'}),'更新插件');assert.equal(stepLabel({operation:'upm_remove'}),'卸载包');assert.equal(stepLabel({operation:'plugin_remove'}),'删除旧插件');
});
test('preview hides Unity registry and builtin rows but keeps full verification inputs',()=>{
  const git={name:'com.magictavern.base',target:{source:'git'},plugins:[]};
  const plan={packages:[git,{name:'com.unity.ide',target:{source:'registry'},plugins:[]},{name:'com.unity.modules.audio',target:{source:'builtin'},plugins:[]}]};
  assert.deepEqual(visiblePlanPackages(plan),[git]);assert.equal(plan.packages.length,3);
});
