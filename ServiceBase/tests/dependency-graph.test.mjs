import test from 'node:test';
import assert from 'node:assert/strict';
import {indexPackages, describeNode} from '../dashboard/static/dashboard/dependency-graph.mjs';
const pkg = (name, depth, children = []) => ({name, depth, tag:'v2', revision:'sha2', dependencies:children.map(name => ({name, requested_tag:'v1'}))});
test('shared dependency remains visible under both parents', () => {
  const graph = indexPackages([pkg('A',0,['S']), pkg('B',0,['S']), pkg('S',1)]);
  assert.deepEqual(graph.roots, ['A','B']);
  assert.equal(describeNode(graph,'S',['A']).shared, true);
  assert.equal(describeNode(graph,'S',['B']).cycle, false);
});
test('cycles, missing nodes and disconnected components stay representable', () => {
  const graph = indexPackages([pkg('A',0,['B','missing']), pkg('B',1,['A']), pkg('C',1,['D']),pkg('D',1,['C'])]);
  assert.deepEqual(graph.roots,['A','C']);
  assert.equal(describeNode(graph,'A',['A','B']).cycle,true);
  assert.equal(describeNode(graph,'missing',['A']).missing,true);
});
test('requested tags are distinct from resolved tags and commit references', () => {
  const graph = indexPackages([pkg('A',0)]);
  assert.equal(describeNode(graph,'A',[],'v1').differs,true);
  assert.equal(describeNode(graph,'A',[],'sha2').differs,false);
  assert.equal(describeNode(graph,'A',[],'v2').differs,false);
});
test('empty snapshot and malformed duplicate names', () => {
  assert.deepEqual(indexPackages([]).roots,[]);
  assert.throws(() => indexPackages([pkg('A',0),pkg('A',1)]));
});
