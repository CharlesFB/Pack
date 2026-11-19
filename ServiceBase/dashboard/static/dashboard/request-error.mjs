export function requestError(error) {
  if (['AbortError','TimeoutError'].includes(error?.name) || /fetch is aborted/i.test(error?.message || ''))
    return '请求超时，正在等待连接恢复；后台任务继续执行。';
  if (error?.name === 'TypeError' && /fetch|network/i.test(error?.message || ''))
    return '暂时无法连接服务，页面将自动重试；后台任务状态会在重连后更新。';
  return error?.message || '请求失败，请重试。';
}
