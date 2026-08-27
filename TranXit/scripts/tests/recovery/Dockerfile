FROM docker:cli@sha256:000bb62ff495f986c9f5578eb67cc2cb98b91138eda81d7762d5371eb8a497fe

RUN apk add --no-cache bash coreutils curl git jq openssl util-linux \
    && mv /usr/local/bin/docker /usr/local/bin/docker-real \
    && mkdir /adapter
COPY . /harness/
RUN chmod +x /harness/*.sh \
    && for command in docker mv rm sync; do ln -s /harness/commands.sh /adapter/$command; done
ENV PATH="/adapter:${PATH}"
ENTRYPOINT ["bash", "/harness/controller.sh"]
