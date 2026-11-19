from django.db import migrations, models


class Migration(migrations.Migration):
    dependencies = [('dashboard', '0013_legacy_migration')]
    operations = [migrations.AddField(
        model_name='installjob', name='history_deleted_at',
        field=models.DateTimeField(blank=True, null=True),
    )]
