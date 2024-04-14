FROM node:8.9.0 as builder

WORKDIR /app
COPY . ./
RUN npm install --production
RUN cd src && npm run generate

FROM nginx:alpine as final
COPY --from=builder /app/build/default.conf /etc/nginx/conf.d/default.conf
COPY --from=builder /app/out /usr/share/nginx/html
