import test from 'node:test';
import assert from 'node:assert/strict';
import {packageDisplayName, packageFlags} from '../dashboard/static/dashboard/package-display.mjs';
test('DisplayName falls back to package name for older Bridge snapshots', () => {
 assert.equal(packageDisplayName({name:'com.test',display_name:'测试包'}),'测试包');
 assert.equal(packageDisplayName({name:'com.test'}),'com.test');
});
test('binary install state uses explicit status then legacy MTP records', () => {
 assert.equal(packageFlags({tag:'test-1-debug',depth:1}).installed,'已安装');
 assert.equal(packageFlags({tag:'test-1',depth:-1,installation_status:'not_installed'}).origin,'—');
 assert.deepEqual(packageFlags({tag:'test-1-debug',depth:1,installation_status:'installed'}),{debug:'Debug',installed:'已安装',origin:'依赖包'});
 assert.deepEqual(packageFlags({tag:'test-1',depth:0,installation_status:'not_installed'}),{debug:'Release',installed:'未安装',origin:'根包'});
});

test('catalog identities retain same-name repositories while dependency graph stays unique', async () => {
 const {packageKey,dependencyPackages}=await import('../dashboard/static/dashboard/package-display.mjs');
 const a={name:'com.example.shared',catalog_id:'1:shared',installation_status:'installed'};
 const b={name:'com.example.shared',catalog_id:'2:shared',installation_status:'not_installed'};
 assert.notEqual(packageKey(a),packageKey(b));
 assert.deepEqual(dependencyPackages([a,b]),[a]);
 assert.deepEqual(dependencyPackages([a,b],b),[b]);
});

test('MTP actions allow updates and restrict uninstall to the installed root tag', async () => {
  const {packageActions} = await import('../dashboard/static/dashboard/package-display.mjs');
  const root = {name:'root',tag:'v1',depth:0,installation_status:'installed'};
  assert.deepEqual(packageActions(root,'v1'),{installLabel:'更新',installDisabled:false,showUninstall:true,uninstallDisabled:false});
  assert.equal(packageActions(root,'v2').uninstallDisabled,true);
  assert.equal(packageActions(root,'v2').installDisabled,false);
  assert.equal(packageActions({...root,depth:1},'v1').showUninstall,false);
  assert.equal(packageActions({...root,name:'com.magictavern.mtpmanager'},'v1').showUninstall,false);
  assert.equal(packageActions(root,'').installDisabled,true);
  assert.equal(packageActions(root,'v1',true).uninstallDisabled,true);
  assert.equal(packageActions({...root,installation_status:'not_installed'},'v1').installLabel,'安装');
});
test('tag preview preserves installation and does not reuse another tag metadata', async () => {
  const {packageAtTag,packageTags} = await import('../dashboard/static/dashboard/package-display.mjs');
  const pkg={tag:'v1',depth:0,installation_status:'installed',description:'old',revision:'commit',available_tags:['v2','v2'],tag_metadata:{v2:{description:'new'}}};
  assert.deepEqual(packageTags(pkg),['v2','v1']);
  assert.equal(packageAtTag(pkg,'v2').description,'new');
  assert.equal(packageAtTag(pkg,'v3').description,'');
  assert.equal(packageAtTag(pkg,'v2').installation_status,'installed');
  assert.equal(packageAtTag(pkg,'v2').revision,'');
  assert.equal(pkg.tag,'v1');
});
