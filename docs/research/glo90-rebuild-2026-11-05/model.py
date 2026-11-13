"""A small learned codec for GLO-90 heights (Balle et al. 2017 style: conv analysis/synthesis with GDN, factorized
entropy model). Heights in km in, km out."""
import math
import torch
import torch.nn as nn
import torch.nn.functional as F


class GDN(nn.Module):
    def __init__(self, c, inverse=False):
        super().__init__()
        self.inverse = inverse
        self.beta = nn.Parameter(torch.ones(c))
        self.gamma = nn.Parameter(0.1 * torch.eye(c))

    def forward(self, x):
        beta = self.beta ** 2 + 1e-6
        gamma = (self.gamma ** 2)[:, :, None, None]
        norm = F.conv2d(x * x, gamma, beta)
        return x * torch.sqrt(norm) if self.inverse else x * torch.rsqrt(norm)


class EntropyBottleneck(nn.Module):
    """Per-channel learned density (non-parametric cumulative), as in Balle et al. 2018 appendix."""

    def __init__(self, c, filters=(3, 3, 3), init_scale=10.0):
        super().__init__()
        f = (1,) + tuple(filters) + (1,)
        scale = init_scale ** (1 / (len(f) - 1))
        self.matrices = nn.ParameterList()
        self.biases = nn.ParameterList()
        self.factors = nn.ParameterList()
        for i in range(len(f) - 1):
            init = math.log(math.expm1(1 / scale / f[i + 1]))
            self.matrices.append(nn.Parameter(torch.full((c, f[i + 1], f[i]), init)))
            self.biases.append(nn.Parameter(torch.rand(c, f[i + 1], 1) - 0.5))
            if i < len(f) - 2:
                self.factors.append(nn.Parameter(torch.zeros(c, f[i + 1], 1)))

    def logits(self, x):
        for i, m in enumerate(self.matrices):
            x = torch.matmul(F.softplus(m), x) + self.biases[i]
            if i < len(self.factors):
                x = x + torch.tanh(self.factors[i]) * torch.tanh(x)
        return x

    def likelihood(self, y_hat):
        b, c, h, w = y_hat.shape
        v = y_hat.permute(1, 0, 2, 3).reshape(c, 1, -1)
        lo, up = self.logits(v - 0.5), self.logits(v + 0.5)
        s = -torch.sign(lo + up).detach()
        lik = torch.abs(torch.sigmoid(s * up) - torch.sigmoid(s * lo)).clamp_min(1e-9)
        return lik.reshape(c, b, h, w).permute(1, 0, 2, 3)

    def forward(self, y):
        y_hat = y + torch.empty_like(y).uniform_(-0.5, 0.5) if self.training else torch.round(y)
        return y_hat, self.likelihood(y_hat)


def conv(i, o):
    return nn.Conv2d(i, o, 5, 2, 2)


def deconv(i, o):
    return nn.ConvTranspose2d(i, o, 5, 2, 2, output_padding=1)


class Codec(nn.Module):
    """Analysis: 4 stride-2 layers (one latent per 16 x 16 GLO-90 points). Input scaled by `scale` (km per unit)."""

    def __init__(self, n=64, m=96):
        super().__init__()
        self.ga = nn.Sequential(conv(1, n), GDN(n), conv(n, n), GDN(n), conv(n, n), GDN(n), conv(n, m))
        self.gs = nn.Sequential(deconv(m, n), GDN(n, True), deconv(n, n), GDN(n, True), deconv(n, n), GDN(n, True),
                                deconv(n, 1))
        self.eb = EntropyBottleneck(m)

    def forward(self, x):
        y = self.ga(x)
        y_hat, lik = self.eb(y)
        return self.gs(y_hat), lik
