FROM node:8.9.0 AS builder

WORKDIR /app
COPY ./package.json ./package.json
RUN npm install --production
COPY . ./
RUN cd src && npm run generate

FROM nginx:alpine AS final
COPY --from=builder /app/build/default.conf /etc/nginx/conf.d/default.conf
COPY --from=builder /app/out /usr/share/nginx/html
