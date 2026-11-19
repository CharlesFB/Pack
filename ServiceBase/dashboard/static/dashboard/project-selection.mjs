let current = null;
const listeners = new Set();
export function selectProject(project) {
  const next = project ? {id:project.instance_id, name:project.project_name, online:project.online, session:project.session_id} : null;
  if (JSON.stringify(next) === JSON.stringify(current)) return;
  current = next;
  for (const listener of listeners) listener(current);
}
export function onProjectSelected(listener) {
  listeners.add(listener); listener(current);
  return () => listeners.delete(listener);
}
