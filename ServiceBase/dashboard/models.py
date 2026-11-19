from django.db import models


class UnityProject(models.Model):
    owner = models.ForeignKey('auth.User', null=True, blank=True, on_delete=models.CASCADE)
    credential = models.ForeignKey('BridgeCredential', null=True, blank=True, on_delete=models.PROTECT)
    transport = models.CharField(max_length=16, default='loopback')
    instance_id = models.CharField(max_length=128, primary_key=True)  # 跨服务重启保留的工程主键，由客户端设备标识与工程路径派生。
    session_id = models.CharField(max_length=128)  # 编辑器运行会话，用于拒绝旧会话的请求或识别历史快照。
    project_name = models.CharField(max_length=256)
    project_path = models.CharField(max_length=2048)
    machine_name = models.CharField(max_length=256)
    unity_version = models.CharField(max_length=64)
    process_id = models.PositiveIntegerField()
    port = models.PositiveIntegerField()
    token = models.CharField(max_length=128)
    last_seen = models.DateTimeField()
    hidden_session = models.CharField(max_length=128, default="", blank=True)  # 只隐藏指定会话的工程记录，新编辑器会话可重新出现。


class BridgeCredential(models.Model):
    owner = models.ForeignKey('auth.User', on_delete=models.CASCADE)
    name = models.CharField(max_length=128)
    digest = models.CharField(max_length=64, unique=True)  # 仅保存设备 token 摘要或快照内容指纹，不保存设备 token 原文。
    active = models.BooleanField(default=True)


class BridgeTask(models.Model):
    import uuid
    id = models.UUIDField(primary_key=True, default=uuid.uuid4, editable=False)
    project = models.ForeignKey(UnityProject, on_delete=models.CASCADE)
    session_id = models.CharField(max_length=128)  # 编辑器运行会话，用于拒绝旧会话的请求或识别历史快照。
    kind = models.CharField(max_length=16)
    payload = models.JSONField(default=dict)
    result = models.JSONField(null=True)
    state = models.CharField(max_length=16, default='pending')
    created_at = models.DateTimeField(auto_now_add=True)
    expires_at = models.DateTimeField()

    class Meta:
        indexes = [models.Index(fields=['project', 'session_id', 'state', 'created_at'])]


class PackageSnapshot(models.Model):
    project = models.OneToOneField(UnityProject, on_delete=models.CASCADE, primary_key=True)
    session_id = models.CharField(max_length=128)  # 编辑器运行会话，用于拒绝旧会话的请求或识别历史快照。
    digest = models.CharField(max_length=64)  # 仅保存设备 token 摘要或快照内容指纹，不保存设备 token 原文。
    payload = models.JSONField()
    received_at = models.DateTimeField(auto_now=True)


class CatalogCache(models.Model):
    key = models.CharField(max_length=64, primary_key=True)  # 目录来源指纹，隔离不同凭据、地址和分支的缓存。
    packages = models.JSONField(default=list)
    revision = models.CharField(max_length=64)
    updated_at = models.DateTimeField(auto_now=True)


class GitLabSettings(models.Model):
    id = models.PositiveSmallIntegerField(primary_key=True, default=1, editable=False)
    url = models.URLField(default='https://gitlab.mte.io/')
    private_token = models.TextField(blank=True)  # 服务器访问 GitLab 的凭据，不回填到网页；数据库备份也须保护。
    ref = models.CharField(max_length=256, default='master')
    blacklist = models.JSONField(default=list)
    debug_mode = models.BooleanField(default=False)
    updated_at = models.DateTimeField(auto_now=True)


class InstallJob(models.Model):
    import uuid
    id = models.UUIDField(primary_key=True, default=uuid.uuid4, editable=False)
    project = models.ForeignKey(UnityProject, on_delete=models.PROTECT)
    request_key = models.UUIDField()
    target = models.JSONField()
    active = models.BooleanField(default=True)
    created_at = models.DateTimeField(auto_now_add=True)
    finished_at = models.DateTimeField(null=True)
    paused = models.BooleanField(default=False)
    cancel_requested = models.BooleanField(default=False)
    expected_fingerprint = models.CharField(max_length=64, default='')
    history_deleted_at = models.DateTimeField(null=True, blank=True)  # 隐藏历史卡片，保留依赖归属和回执证据。

    @property
    def step(self):
        rows = sorted(self.steps.all(), key=lambda s: s.position)
        return next((s for s in rows if s.state not in ('succeeded', 'skipped', 'cancelled')), rows[-1])

    class Meta:
        constraints = [models.UniqueConstraint(fields=['project', 'request_key'], name='install_request_once'),
                       models.UniqueConstraint(fields=['project'], condition=models.Q(active=True), name='install_one_active_project')]


class InstallStep(models.Model):
    job = models.ForeignKey(InstallJob, on_delete=models.CASCADE, related_name='steps')
    position = models.PositiveIntegerField(default=0)
    target = models.JSONField(default=dict)
    state = models.CharField(max_length=24, default='queued')
    updated_at = models.DateTimeField(auto_now=True)

    class Meta:
        constraints = [models.UniqueConstraint(fields=['job', 'position'], name='install_step_position')]

    @property
    def command(self):
        return self.target or self.job.target

    @property
    def attempt(self):
        return max(self.attempts.all(), key=lambda item: item.number)


class InstallAttempt(models.Model):
    import uuid
    id = models.UUIDField(primary_key=True, default=uuid.uuid4, editable=False)
    step = models.ForeignKey(InstallStep, on_delete=models.CASCADE, related_name='attempts')
    number = models.PositiveIntegerField(default=1)
    retry_key = models.UUIDField(null=True)
    retry_reason = models.CharField(max_length=500, default='')
    requested_by = models.ForeignKey('auth.User', null=True, on_delete=models.SET_NULL)
    destination_root = models.CharField(max_length=512, default='')
    dispatched_at = models.DateTimeField(null=True)
    sequence = models.PositiveIntegerField(default=0)
    recheck = models.PositiveIntegerField(default=0)
    report = models.JSONField(default=dict)

    class Meta:
        constraints = [models.UniqueConstraint(fields=['step', 'number'], name='install_attempt_number'),
                       models.UniqueConstraint(fields=['step', 'retry_key'], name='install_retry_once')]


class InstallEvent(models.Model):
    attempt = models.ForeignKey(InstallAttempt, on_delete=models.CASCADE, related_name='events')
    sequence = models.PositiveIntegerField()
    report = models.JSONField()
    created_at = models.DateTimeField(auto_now_add=True)

    class Meta:
        constraints = [models.UniqueConstraint(fields=['attempt', 'sequence'], name='install_event_once')]


class PluginReceipt(models.Model):
    attempt = models.OneToOneField(InstallAttempt, primary_key=True, on_delete=models.CASCADE)
    digest = models.CharField(max_length=64)
    files = models.JSONField()
    received_at = models.DateTimeField(auto_now_add=True)


class PluginInstallation(models.Model):
    project = models.ForeignKey(UnityProject, on_delete=models.PROTECT)
    plugin_id = models.CharField(max_length=160)
    build_id = models.CharField(max_length=64)
    receipt = models.ForeignKey(PluginReceipt, on_delete=models.PROTECT)
    verified_at = models.DateTimeField(auto_now=True)

    class Meta:
        constraints = [models.UniqueConstraint(fields=['project','plugin_id','build_id'], name='plugin_installation_build')]


class PluginInventory(models.Model):
    project = models.OneToOneField(UnityProject, primary_key=True, on_delete=models.CASCADE)
    session_id = models.CharField(max_length=128, default='')
    request_key = models.CharField(max_length=36, default='initial')
    payload = models.JSONField(default=dict)
    received_at = models.DateTimeField(auto_now=True)


class PluginRemovalReceipt(models.Model):
    attempt = models.OneToOneField(InstallAttempt, primary_key=True, on_delete=models.CASCADE)
    digest = models.CharField(max_length=64)
    payload = models.JSONField()
    received_at = models.DateTimeField(auto_now_add=True)


class ProjectPackageState(models.Model):
    project = models.OneToOneField(UnityProject, primary_key=True, on_delete=models.CASCADE)
    session_id = models.CharField(max_length=128)
    payload = models.JSONField(default=dict)
    received_at = models.DateTimeField(auto_now=True)


class InstallationPlan(models.Model):
    import uuid
    id = models.UUIDField(primary_key=True, default=uuid.uuid4, editable=False)
    project = models.ForeignKey(UnityProject, on_delete=models.CASCADE)
    request_key = models.UUIDField()
    root = models.JSONField()
    session_id = models.CharField(max_length=128)
    state = models.CharField(max_length=20, default='resolving')
    payload = models.JSONField(default=dict)
    error = models.CharField(max_length=500, default='')
    created_at = models.DateTimeField(auto_now_add=True)
    job = models.OneToOneField(InstallJob, null=True, on_delete=models.PROTECT)
    selection = models.JSONField(default=dict)

    class Meta:
        constraints = [models.UniqueConstraint(fields=['project', 'request_key'], name='installation_plan_request')]


class PluginUpdateReceipt(models.Model):
    attempt = models.OneToOneField(InstallAttempt, on_delete=models.PROTECT, primary_key=True)
    digest = models.CharField(max_length=64)
    payload = models.JSONField()
    received_at = models.DateTimeField(auto_now_add=True)


class LegacyMigration(models.Model):
    import uuid
    id = models.UUIDField(primary_key=True, default=uuid.uuid4, editable=False)
    project = models.ForeignKey(UnityProject, on_delete=models.PROTECT)
    session_id = models.CharField(max_length=128)
    request_key = models.UUIDField()
    state = models.CharField(max_length=16, default='pending')
    payload = models.JSONField(default=dict)
    digest = models.CharField(max_length=64, default='')
    job = models.OneToOneField(InstallJob, null=True, on_delete=models.PROTECT)
    created_at = models.DateTimeField(auto_now_add=True)
    updated_at = models.DateTimeField(auto_now=True)

    class Meta:
        constraints = [models.UniqueConstraint(fields=['project', 'request_key'], name='legacy_migration_request_once')]
