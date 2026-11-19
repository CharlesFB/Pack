export const installationLabels = {
  waiting_input:'等待目录', cancelled:'已取消', paused:'已暂停', stopping:'正在结束当前步骤',
  skipped:'已跳过', queued:'待执行', accepted:'等待编辑器空闲', running:'正在执行',
  verifying:'正在核对结果', needs_review:'需要核对', waiting_connection:'等待执行器连接',
  succeeded:'已完成', failed:'失败'
};
export const installationLabel = state => installationLabels[state] || state;
export function installationTone(job) {
  if (!job.active && (job.cancel_requested || job.display_state === 'cancelled')) return 'cancelled';
  if (['failed','needs_review'].includes(job.display_state)) return 'failed';
  if (!job.active && job.display_state === 'succeeded') return 'succeeded';
  return 'running';
}
export function installationProgress(job) {
  const steps = job.steps || [];
  const completed = steps.filter(step => step.state === 'succeeded').length;
  const skipped = steps.filter(step => step.state === 'skipped').length;
  const total = steps.length;
  return {completed, skipped, total, finished:completed + skipped,
    percent:total ? Math.round((completed + skipped) * 100 / total) : 0};
}
export function installationStepTone(step, job) {
  if (['succeeded','skipped','cancelled'].includes(step.state)) return 'muted';
  if (['failed','needs_review'].includes(step.state)) return 'failed';
  if (job.active && step.attempt_id === job.attempt_id) return 'current';
  return 'pending';
}
export function installationKind(target) {
  return target.operation === 'plan'
    ? ({install:'安装计划',update:'包更新计划',uninstall:'卸载计划',plugin_update:'插件更新计划'}[target.plan_action] || '安装计划')
    : ({plugin:'插件导入',plugin_remove:'插件删除',plugin_update:'插件更新',upm_remove:'包卸载',legacy_migrate:'旧记录迁移'}[target.operation] || '包安装');
}
