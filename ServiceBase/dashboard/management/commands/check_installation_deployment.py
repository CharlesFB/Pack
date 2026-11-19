"""Verify the deployed installation stack without creating jobs or touching Unity."""
import hashlib
import json
import re
import tempfile
import uuid
from pathlib import Path

from django.conf import settings
from django.core.cache import cache
from django.core.management.base import BaseCommand, CommandError
from django.db import connection
from django.db.migrations.executor import MigrationExecutor


class Command(BaseCommand):
    help = 'Check production schema, Redis, plugin storage, static assets and executor files.'

    def handle(self, *args, **options):
        if settings.LOCAL_MODE or connection.vendor != 'postgresql':
            raise CommandError('请在 Docker 生产配置中执行部署检查。')
        executor = MigrationExecutor(connection)
        if executor.migration_plan(executor.loader.graph.leaf_nodes()):
            raise CommandError('数据库仍有未应用迁移。')
        from dashboard.models import InstallationPlan, PluginUpdateReceipt, ProjectPackageState, LegacyMigration
        for model in (InstallationPlan, PluginUpdateReceipt, ProjectPackageState, LegacyMigration):
            model.objects.exists()
        key = 'deployment-check:' + uuid.uuid4().hex
        try:
            cache.set(key, 'ready', 30)
            if cache.get(key) != 'ready':
                raise CommandError('Redis 读写检查失败。')
        finally:
            cache.delete(key)
        try:
            from dashboard.plugin_api import snapshot
            index, plugins = snapshot()
            builds = [b for p in plugins for b in p['builds']]
            if any(b['availability'] != 'available' for b in builds):
                raise CommandError('插件仓库有缺失、大小不符或元数据无效的制品。')
            with tempfile.TemporaryFile(dir=settings.PLUGIN_DELIVERY_ROOT) as stream:
                stream.write(b'deployment-check')
                stream.flush()
            assets = ('app.js', 'plugins.js', 'installations.js', 'installation-plan.mjs',
                      'zip-destination.mjs', 'project-selection.mjs', 'legacy-migration.js')
            for name in assets:
                source = settings.BASE_DIR / 'dashboard/static/dashboard' / name
                deployed = Path(settings.STATIC_ROOT) / 'dashboard' / name
                if hashlib.sha256(source.read_bytes()).digest() != hashlib.sha256(deployed.read_bytes()).digest():
                    raise CommandError('静态文件不是当前版本：' + name)
            directory = settings.BASE_DIR / 'unity/UpmExecutor'
            version = re.search(r'executor_version\s*=\s*(\d+)', (directory/'UpmExecutor.cs').read_text())
            if not version or int(version[1]) < 9 or not all((directory/name).is_file() for name in (
                    'AssetsPackages.cs', 'LegacyMigration.cs', 'PackageRemover.cs', 'PluginUpdater.cs', 'ProjectPackageState.cs', 'ServiceBase.UpmExecutor.asmdef')):
                raise CommandError('镜像缺少完整 v9 执行器源码。')
        except (OSError, ValueError, KeyError, TypeError) as error:
            raise CommandError('插件存储、静态资源或执行器文件检查失败。') from error
        self.stdout.write(json.dumps(dict(
            database='postgresql', migrations='current', cache='ready', delivery='writable',
            plugins=len(plugins), builds=len(builds), mappings=len(index['package_mappings']),
            static_files='current', executor_version=int(version[1])), ensure_ascii=False))
