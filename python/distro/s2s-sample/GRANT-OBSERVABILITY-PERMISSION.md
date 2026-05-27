# Grant S2S Observability Permission

The agent instance needs the `Agent365.Observability.OtelWrite` app role for S2S export to work.

## 1. Login to your tenant

```bash
az login --tenant <tenant-id> --use-device-code
```

## 2. Find the observability resource ID in your tenant

```bash
az rest --method GET \
  --url "https://graph.microsoft.com/v1.0/servicePrincipals(appId='9b975845-388f-4429-889e-eab1ef63949c')?%24select=id"
```

Note the `id` from the response -- use it as `<resource-id>` below.

## 3. Assign the role

```bash
az rest --method POST \
  --url "https://graph.microsoft.com/v1.0/servicePrincipals/<agent-instance-id>/appRoleAssignments" \
  --body '{
    "principalId": "<agent-instance-id>",
    "resourceId": "<resource-id>",
    "appRoleId": "8f71190c-00c8-461d-a63b-f74abde9ba52"
  }'
```

- `<agent-instance-id>` -- from `recipient.agenticAppId` in the activity
- `<resource-id>` -- from step 2
- `appRoleId` is always `8f71190c-00c8-461d-a63b-f74abde9ba52`

Role propagation may take a few minutes. Initial 401s from the export endpoint are expected.
