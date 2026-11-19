// Pure graph helpers. Shared dependencies remain visible under every parent.
/**
 * 构造依赖索引、引用计数和根节点，保留断开的循环组件。
 */
export function indexPackages(
  packages // 候选包集合
) {
  const index = new Map();
  for (const p of packages) {
    if (!p || typeof p.name !== 'string' || !p.name || !Array.isArray(p.dependencies)) throw new Error('MTP 包数据格式无效');
    if (index.has(p.name)) throw new Error(`MTP 快照有重复包名：${p.name}`);
    index.set(p.name, p);
    for (const edge of p.dependencies) if (!edge || typeof edge.name !== 'string' || !edge.name) throw new Error('MTP 依赖数据格式无效');
  }
  const incoming = new Map();
  for (const p of index.values()) for (const edge of p.dependencies) incoming.set(edge.name, (incoming.get(edge.name) || 0) + 1);
  const roots = [...index.values()].filter(p => p.depth === 0 || !incoming.has(p.name)).map(p => p.name);
  const reached = new Set();
  /**
   * 迭代标记根节点可达的依赖，避免循环导致递归溢出。
   */
  function mark(
    name // 依赖包名
  ) {
    const queue = [name];
    while (queue.length) {
      const current = queue.pop();
      if (reached.has(current)) continue;
      reached.add(current);
      for (const edge of index.get(current)?.dependencies || []) queue.push(edge.name);
    }
  }
  roots.forEach(mark);
  // Retain disconnected/cyclic components even if none has Depth == 0.
  for (const name of index.keys()) if (!reached.has(name)) { roots.push(name); mark(name); }
  return {index, incoming, roots};
}
/**
 * 计算节点的缺失、循环、共享和标签差异提示。
 */
export function describeNode(
  graph, // 依赖图索引及引用计数
  name, // 依赖包名
  ancestors = [], // 当前路径的祖先包名，用于检测循环
  requestedTag = '' // 父节点要求的依赖标签
) {
  const pkg = graph.index.get(name);
  return {pkg, missing: !pkg, cycle: ancestors.includes(name), shared: (graph.incoming.get(name) || 0) > 1,
    differs: !!pkg && !!requestedTag && requestedTag !== pkg.tag && requestedTag !== pkg.revision};
}
