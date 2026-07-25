FROM node:20-slim

WORKDIR /app

# 后端
COPY backend/package.json backend/package-lock.json* ./backend/
RUN cd backend && npm install --production

# 前端构建
COPY frontend/package.json frontend/package-lock.json* ./frontend/
RUN cd frontend && npm install

COPY . .
RUN cd frontend && npm run build
RUN cd backend && npx prisma generate

# 构建后端
RUN cd backend && npx tsc

# 复制前端构建产物到后端静态目录
RUN cp -r frontend/dist backend/public

EXPOSE 3001

WORKDIR /app/backend
CMD ["sh", "-c", "npx prisma db push && node dist/app.js"]
