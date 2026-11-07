# Multi-stage Dockerfile for ERP KHO Frontend (React Vite + Non-Root Nginx)
FROM node:20-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build

# Hardened non-root production serve stage (UID/GID 101, port 8080)
FROM nginxinc/nginx-unprivileged:1.27-alpine AS final
COPY --from=build --chown=101:101 /app/dist /usr/share/nginx/html
COPY --chown=101:101 nginx.conf /etc/nginx/conf.d/default.conf
RUN sed -i 's/listen\s*80;/listen 8080;/g' /etc/nginx/conf.d/default.conf

USER 101
EXPOSE 8080
CMD ["nginx", "-g", "daemon off;"]
