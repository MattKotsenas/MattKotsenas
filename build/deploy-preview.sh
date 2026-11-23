#!/bin/sh
set -eu

# Aspire 17 overrides AllowResourceGroupCreation during deploy. Fail before invoking it if the group is absent.
az group show --name blog --subscription "${Azure__SubscriptionId:?Azure__SubscriptionId is required}" --only-show-errors --output none
exec dotnet aspire deploy --environment preview --non-interactive
