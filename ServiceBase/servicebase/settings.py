from pathlib import Path
import os
from django.core.exceptions import ImproperlyConfigured

BASE_DIR = Path(__file__).resolve().parent.parent
PLUGIN_REPOSITORY_ROOT = Path(os.environ.get('PLUGIN_REPOSITORY_ROOT', BASE_DIR / 'external'))
PLUGIN_DELIVERY_ROOT = Path(os.environ.get('PLUGIN_DELIVERY_ROOT', BASE_DIR / '.runtime/plugin-deliveries'))
# Local development only. Do not expose runserver to a LAN or public network.
SECRET_KEY = "servicebase-local-development-only"
DEBUG = True
ALLOWED_HOSTS = ["127.0.0.1", "localhost", "[::1]"]
INSTALLED_APPS = ["django.contrib.auth", "django.contrib.contenttypes", "django.contrib.sessions", "django.contrib.staticfiles", "dashboard"]
MIDDLEWARE = [
    "django.middleware.security.SecurityMiddleware",
    "django.middleware.common.CommonMiddleware",
    "django.contrib.sessions.middleware.SessionMiddleware",
    "django.contrib.auth.middleware.AuthenticationMiddleware",
    "dashboard.auth.AccessMiddleware",
    "django.middleware.csrf.CsrfViewMiddleware",
    "django.middleware.clickjacking.XFrameOptionsMiddleware",
]
ROOT_URLCONF = "servicebase.urls"
TEMPLATES = [{
    "BACKEND": "django.template.backends.django.DjangoTemplates",
    "DIRS": [], "APP_DIRS": True,
    "OPTIONS": {"context_processors": ["django.template.context_processors.csrf"]},
}]
DATABASES = {"default": {"ENGINE": "django.db.backends.sqlite3", "NAME": BASE_DIR / "db.sqlite3"}}
DEFAULT_AUTO_FIELD = "django.db.models.BigAutoField"
STATIC_URL = "static/"
TIME_ZONE = "Asia/Shanghai"
USE_TZ = True
DATA_UPLOAD_MAX_MEMORY_SIZE = 3 * 1024 * 1024


LOCAL_MODE = os.environ.get("SERVICEBASE_MODE", "local") == "local"  # 本地开发使用回环地址和匿名无归属工程；生产入口必须关闭此模式。
STATIC_ROOT = BASE_DIR / "staticfiles"
LOGIN_URL = "/accounts/login/"
LOGIN_REDIRECT_URL = "/"
LOGOUT_REDIRECT_URL = LOGIN_URL
CACHES = {"default": {"BACKEND": "django.core.cache.backends.locmem.LocMemCache", "OPTIONS": {"MAX_ENTRIES": 10000}}}  # 开发缓存仅在单进程内有效，生产分支替换为跨进程 Redis。
if not LOCAL_MODE:
    DEBUG = False
    required = ("SECRET_KEY", "ALLOWED_HOSTS", "POSTGRES_DB", "POSTGRES_USER", "POSTGRES_PASSWORD", "POSTGRES_HOST", "REDIS_URL")
    for key in required:
        if not os.environ.get(key):
            raise ImproperlyConfigured("Missing production environment variable: " + key)
    SECRET_KEY = os.environ["SECRET_KEY"]
    ALLOWED_HOSTS = os.environ["ALLOWED_HOSTS"].split(",")
    DATABASES = {"default": {"ENGINE": "django.db.backends.postgresql", "NAME": os.environ["POSTGRES_DB"],
        "USER": os.environ["POSTGRES_USER"], "PASSWORD": os.environ["POSTGRES_PASSWORD"],
        "HOST": os.environ["POSTGRES_HOST"], "PORT": os.environ.get("POSTGRES_PORT", "5432"), "CONN_MAX_AGE": 60}}
    CACHES = {"default": {"BACKEND": "django.core.cache.backends.redis.RedisCache", "LOCATION": os.environ["REDIS_URL"]}}  # 开发缓存仅在单进程内有效，生产分支替换为跨进程 Redis。
    SESSION_COOKIE_SECURE = True
    CSRF_COOKIE_SECURE = True
    SECURE_SSL_REDIRECT = True
    SECURE_PROXY_SSL_HEADER = ("HTTP_X_FORWARDED_PROTO", "https")  # 仅信任受控反向代理写入的协议头，Web 容器不得直接对公网开放。
    SECURE_HSTS_SECONDS = 3600
    CSRF_TRUSTED_ORIGINS = ["https://" + host for host in ALLOWED_HOSTS]

# Registry metadata fetches never follow arbitrary project-controlled hosts.
SERVICEBASE_ALLOWED_REGISTRIES = [u.rstrip('/') for u in os.environ.get('SERVICEBASE_ALLOWED_REGISTRIES', 'https://packages.unity.com').split(',') if u]
