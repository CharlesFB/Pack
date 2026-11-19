import test from 'node:test';
import assert from 'node:assert/strict';
import {installationTone,installationProgress,installationStepTone} from '../dashboard/static/dashboard/installation-history.mjs';

test('card status distinguishes success, failed, cancelled and still stopping',()=>{
  assert.equal(installationTone({display_state:'succeeded',active:false}),'succeeded');
  assert.equal(installationTone({display_state:'failed',active:true}),'failed');
  assert.equal(installationTone({display_state:'needs_review',active:true}),'failed');
  assert.equal(installationTone({display_state:'cancelled',active:false,cancel_requested:true}),'cancelled');
  assert.equal(installationTone({display_state:'stopping',active:true,cancel_requested:true}),'running');
  assert.equal(installationTone({display_state:'waiting_connection',active:true}),'running');
});
test('step progress counts only completed and skipped work, not running or failed work',()=>{
  const steps=['succeeded','skipped','running','queued','failed','cancelled'].map(state=>({state}));
  assert.deepEqual(installationProgress({steps}),{completed:1,skipped:1,finished:2,total:6,percent:33});
  assert.equal(installationProgress({steps:[]}).percent,0);
  assert.equal(installationProgress({steps:[{state:'succeeded'},{state:'skipped'}]}).percent,100);
});
test('completed work is muted and only the current unfinished step is highlighted',()=>{
  const job={active:true,attempt_id:'current'};
  assert.equal(installationStepTone({state:'running',attempt_id:'current'},job),'current');
  assert.equal(installationStepTone({state:'queued',attempt_id:'next'},job),'pending');
  assert.equal(installationStepTone({state:'succeeded',attempt_id:'current'},job),'muted');
  assert.equal(installationStepTone({state:'skipped'},job),'muted');
  assert.equal(installationStepTone({state:'failed',attempt_id:'current'},job),'failed');
  assert.equal(installationStepTone({state:'queued',attempt_id:'current'},{...job,active:false}),'pending');
});
