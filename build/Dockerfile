FROM node:8.9.0 as builder

WORKDIR /app
COPY . ./
RUN npm install --production
RUN cd src && npm test

FROM flashspys/nginx-static as final
RUN apk update && apk upgrade
COPY --from=builder /app/out /static
