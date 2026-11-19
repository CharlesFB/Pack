/**
 * 获取包的展示名称，缺失时回退到包名。
 */
export function packageDisplayName(
  pkg // 包记录
) { return pkg.display_name || pkg.name || '未命名包'; }
/**
 * 将标签、安装证据和依赖深度映射为列表状态。
 */
export function packageFlags(
  pkg // 包记录
) {
  return {
    debug: !pkg.tag ? '未知' : pkg.tag.endsWith('-debug') ? 'Debug' : 'Release',
    installed: pkg.installation_status === 'not_installed' ? '未安装' : pkg.installation_status === 'installed' || (Number.isInteger(pkg.depth) && pkg.depth >= 0) ? '已安装' : '未安装',
    origin: Number.isInteger(pkg.depth) ? pkg.depth === 0 ? '根包' : pkg.depth > 0 ? '依赖包' : '—' : '未知'
  };
}

/**
 * 生成目录条目的选择标识，避免不同仓库的同名包互相覆盖。
 */
export function packageKey(
  pkg // 包记录
) { return pkg.catalog_id || pkg.name; }
/**
 * 按依赖包名建立唯一节点集合，优先保留已安装记录或显式选中版本。
 */
export function dependencyPackages(
  packages, // 候选包集合
  selected // 显式选中的包记录
) {
  const byName = new Map();
  for (const pkg of packages) if (!byName.has(pkg.name) || pkg.installation_status === 'installed') byName.set(pkg.name,pkg);
  if (selected) byName.set(selected.name,selected);
  return [...byName.values()];
}

/**
 * 合并可选标签与当前标签并去重，保留目录中已移除的安装标签。
 */
export function packageTags(
  pkg // 包记录
) {
  return [...new Set([...(pkg.available_tags || []), pkg.tag].filter(t => typeof t === 'string' && t))];
}
/**
 * 按 MTP 规则计算安装或更新按钮状态，以及根包卸载的显示与可用条件。
 */
export function packageActions(
  pkg, // 包记录
  tag, // 所选版本标签或 DOM 标签名
  busy = false // 是否正在刷新或执行操作
) {
  const installed = packageFlags(pkg).installed === '已安装';
  const self = pkg.name === 'com.magictavern.mtpmanager';
  const showUninstall = installed && pkg.depth === 0 && !self && !pkg.is_local;
  return {installLabel:installed ? '更新' : '安装', installDisabled:!tag || busy,
    showUninstall, uninstallDisabled:!showUninstall || busy || tag !== pkg.tag};
}
/**
 * 构造所选标签的详情视图，不修改真实安装记录；缺少元数据时清空旧标签详情。
 */
export function packageAtTag(
  pkg, // 包记录
  tag // 所选版本标签或 DOM 标签名
) {
  if (tag === pkg.tag) return pkg;
  const blank = {display_name:'',version:'',description:'',author:'',unity:'',license:'',changelog:'',dependencies:[]};
  return {...pkg,...blank,...(pkg.tag_metadata?.[tag] || {}),tag,revision:''};
}
