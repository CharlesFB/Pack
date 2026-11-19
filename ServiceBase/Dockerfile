FROM python:3.12-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY requirements*.txt ./
RUN pip install --no-cache-dir -r requirements-production.txt
COPY . .
RUN useradd --uid 10001 --create-home servicebase \
    && mkdir -p /app/staticfiles /app/.runtime/plugin-deliveries \
    && chown -R servicebase:servicebase /app/staticfiles /app/.runtime
USER servicebase
CMD ["gunicorn", "servicebase.wsgi:application", "--bind", "0.0.0.0:8000", "--workers", "4", "--threads", "4", "--timeout", "150", "--access-logfile", "-"]
