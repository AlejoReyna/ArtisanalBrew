# GitHub Actions EC2 Deploy Troubleshooting

The deploy workflow copies the published web app to EC2 over SSH/SCP. If the job fails with:

```text
ssh: connect to host *** port 22: Connection timed out
scp: Connection closed
```

the runner cannot reach the EC2 instance on TCP port 22. This happens before SSH key authentication, so rotating the private key will not fix it.

## Check the basics

1. Confirm the EC2 instance is running.
2. Confirm the `EC2_HOST` repository secret is the instance's current public IPv4 address or public DNS name.
3. Confirm `EC2_USER` is set, usually `ubuntu` for an Ubuntu AMI.
4. If SSH uses a non-default port, set the `EC2_SSH_PORT` repository secret.

## Most likely cause

The original EC2 setup opened port 22 only to the public IP of the local machine that created the security group:

```sh
export MY_IP=$(curl -s https://checkip.amazonaws.com)/32
aws ec2 authorize-security-group-ingress \
  --region "$AWS_REGION" \
  --group-id "$EC2_SG_ID" \
  --protocol tcp \
  --port 22 \
  --cidr "$MY_IP"
```

GitHub-hosted runners use different public IPs, so they are blocked by that rule.

## Fix options

### Option A: Deploy from a self-hosted runner

Run a self-hosted GitHub Actions runner from a stable network that is allowed by the EC2 security group. This keeps SSH locked down to a known source IP.

### Option B: Temporarily allow the GitHub runner IP

Add AWS credentials to the workflow and authorize the current runner's public IP before SCP, then revoke it after deployment. Use a tightly scoped IAM policy that can only edit this EC2 security group.

The commands are:

```sh
RUNNER_IP=$(curl -s https://checkip.amazonaws.com)/32

aws ec2 authorize-security-group-ingress \
  --region "$AWS_REGION" \
  --group-id "$EC2_SG_ID" \
  --protocol tcp \
  --port 22 \
  --cidr "$RUNNER_IP"

# Run scp/ssh deployment here.

aws ec2 revoke-security-group-ingress \
  --region "$AWS_REGION" \
  --group-id "$EC2_SG_ID" \
  --protocol tcp \
  --port 22 \
  --cidr "$RUNNER_IP"
```

### Option C: Open SSH more broadly

Allowing `0.0.0.0/0` on port 22 will make the workflow connect, but it exposes SSH to the internet. If you use this temporarily, keep key-only authentication enabled and remove the rule immediately after testing.

## Interpreting errors

- `Connection timed out`: network path is blocked, usually security group, NACL, wrong host, stopped instance, or no public IP.
- `Permission denied (publickey)`: network is reachable, but `EC2_USER` or `EC2_SSH_PRIVATE_KEY` is wrong.
- `Connection refused`: network is reachable, but sshd is not listening on that port.
