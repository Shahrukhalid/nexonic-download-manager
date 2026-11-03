FROM python:3.12-slim
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY requirements.txt app.py ./
RUN pip install --no-cache-dir -r requirements.txt
ENV PORT=8080
# yt-dlp changes often because sites change: update it every time the server starts.
CMD pip install --no-cache-dir -q -U yt-dlp; exec gunicorn -b 0.0.0.0:${PORT} -w 2 -k gthread --threads 8 --timeout 900 app:app
