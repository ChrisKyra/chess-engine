// Texel tuning: fits the evaluation's weights to game results.
//
// Built only into the `tuner` binary (make tuner), which is the engine compiled
// with EVAL_TRACE so that the evaluation reports how often it used every weight
// (see the trace in eval.cpp).  Usage:
//
//   ./tuner tune positions.txt [epochs] [output]
//
// positions.txt holds one position per line, "FEN;result" with the result from
// White's point of view (1.0, 0.5 or 0.0) -- the format the match runner's
// --datagen mode writes.  The tuned weights are written to `output` (default
// tuned.txt) as C++ definitions in the same form as eval.cpp.
//
// How it works.  The evaluation is, apart from a few non-linear parts (the
// king-danger formula, the endgame scale factor, tempo), a sum of
// coefficient x weight, blended between a middlegame and an endgame total by
// the game phase.  The trace gives the coefficients of every position once;
// after that, the evaluation for any set of weights is a cheap dot product, the
// non-linear parts held fixed at what they were.  The tuner then minimises the
// mean squared error between each game's result and the evaluation turned into
// an expected score, 1 / (1 + 10^(-K * eval / 400)), with K fitted first so the
// current weights' scale is respected.  The weights follow the gradient with
// Adam, every position in every step; a tenth of the positions are held out to
// show whether the fit still generalises.

#ifdef EVAL_TRACE

#include "eval.h"
#include "position.h"

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <fstream>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

namespace Tune {

namespace {

// Every position, flattened: its coefficients are entries [begin[i], begin[i+1]).
struct Data {
    std::vector<uint32_t> begin{0};
    std::vector<uint16_t> index;
    std::vector<float> coefficient;
    std::vector<float> mg_weight;   // phase / 24: how much the middlegame total counts
    std::vector<float> eg_weight;   // (24 - phase) / 24 * scale / 64: how much the endgame total counts
    std::vector<float> fixed_mg;    // the part of the middlegame total no weight explains (king danger...)
    std::vector<float> fixed_eg;
    std::vector<float> tempo;
    std::vector<float> result;

    size_t size() const { return result.size(); }
};

double sigmoid(double k, double eval) { return 1.0 / (1.0 + std::pow(10.0, -k * eval / 400.0)); }

// The evaluation of position i (White's view) for the weights mg/eg.
double evaluate(const Data& d, size_t i, const std::vector<double>& mg, const std::vector<double>& eg) {
    double m = d.fixed_mg[i], e = d.fixed_eg[i];
    for (uint32_t j = d.begin[i]; j < d.begin[i + 1]; ++j) {
        m += d.coefficient[j] * mg[d.index[j]];
        e += d.coefficient[j] * eg[d.index[j]];
    }
    return d.mg_weight[i] * m + d.eg_weight[i] * e + d.tempo[i];
}

// Runs fn(first, last, thread) over the positions on every core and waits.
template<typename F>
void parallel(size_t n, int threads, F fn) {
    std::vector<std::thread> pool;
    for (int t = 0; t < threads; ++t)
        pool.emplace_back([=] { fn(n * t / threads, n * (t + 1) / threads, t); });
    for (auto& th : pool) th.join();
}

double mean_error(const Data& d, double k, const std::vector<double>& mg, const std::vector<double>& eg, int threads) {
    std::vector<double> sums(threads, 0.0);
    parallel(d.size(), threads, [&](size_t a, size_t b, int t) {
        double s = 0;
        for (size_t i = a; i < b; ++i) {
            const double err = d.result[i] - sigmoid(k, evaluate(d, i, mg, eg));
            s += err * err;
        }
        sums[t] = s;
    });
    double total = 0;
    for (double s : sums) total += s;
    return total / double(d.size());
}

// Reads the positions and traces each one.  Every tenth goes to `test`.
bool load(const std::string& path, Data& train, Data& test, const std::vector<Eval::Tune::Param>& params,
          double& max_model_error) {
    std::ifstream in(path);
    if (!in) {
        std::cerr << "cannot open " << path << "\n";
        return false;
    }
    std::string line;
    Position pos;
    Eval::Tune::Trace tr;
    size_t count = 0, skipped = 0;
    max_model_error = 0;
    while (std::getline(in, line)) {
        const size_t semi = line.rfind(';');
        if (semi == std::string::npos || !pos.set_from_fen(line.substr(0, semi))) { ++skipped; continue; }
        const double result = std::stod(line.substr(semi + 1));
        Eval::Tune::trace(pos, tr);

        Data& d = (count++ % 10 == 9) ? test : train;
        double lin_mg = 0, lin_eg = 0;
        for (auto [i, c] : tr.coefficients) {
            d.index.push_back(uint16_t(i));
            d.coefficient.push_back(float(c));
            lin_mg += c * params[i].mg;
            lin_eg += c * params[i].eg;
        }
        d.begin.push_back(uint32_t(d.index.size()));
        d.mg_weight.push_back(float(tr.phase / 24.0));
        d.eg_weight.push_back(float((24 - tr.phase) / 24.0 * tr.scale / 64.0));
        d.fixed_mg.push_back(float(tr.mg - lin_mg));
        d.fixed_eg.push_back(float(tr.eg - lin_eg));
        d.tempo.push_back(float(tr.tempo));
        d.result.push_back(float(result));

        // The model, with the current weights, must reproduce the evaluation
        // (up to the engine's integer rounding).
        const double model = d.mg_weight.back() * tr.mg + d.eg_weight.back() * tr.eg + tr.tempo;
        max_model_error = std::max(max_model_error, std::abs(model - tr.white_score));
    }
    std::cout << "positions: " << train.size() << " to tune on, " << test.size() << " held out"
              << (skipped ? ", " + std::to_string(skipped) + " unreadable lines skipped" : "") << "\n";
    return train.size() > 0;
}

} // namespace

int run(int argc, char** argv) {
    if (argc < 3) {
        std::cerr << "usage: tuner tune positions.txt [epochs] [output]\n";
        return 1;
    }
    const std::string path = argv[2];
    const int epochs = argc > 3 ? std::max(1, std::atoi(argv[3])) : 2000;
    const std::string output = argc > 4 ? argv[4] : "tuned.txt";
    const int threads = std::max(1u, std::thread::hardware_concurrency());
    const auto start = std::chrono::steady_clock::now();
    auto seconds = [&] {
        return std::chrono::duration<double>(std::chrono::steady_clock::now() - start).count();
    };

    const auto params = Eval::Tune::parameters();
    const size_t n = params.size();
    std::vector<double> mg(n), eg(n);
    for (size_t i = 0; i < n; ++i) { mg[i] = params[i].mg; eg[i] = params[i].eg; }

    Data train, test;
    double max_model_error = 0;
    if (!load(path, train, test, params, max_model_error)) return 1;
    std::printf("traced in %.1f s; largest difference between the model and the evaluation: %.1f cp\n",
                seconds(), max_model_error);

    // K: the scale that best turns the current evaluation into expected scores.
    double lo = 0.1, hi = 3.0;
    for (int it = 0; it < 40; ++it) {
        const double a = lo + (hi - lo) / 3, b = hi - (hi - lo) / 3;
        if (mean_error(train, a, mg, eg, threads) < mean_error(train, b, mg, eg, threads)) hi = b;
        else lo = a;
    }
    const double k = (lo + hi) / 2;
    std::printf("K = %.4f; error before tuning: %.6f (held out %.6f)\n", k,
                mean_error(train, k, mg, eg, threads), mean_error(test, k, mg, eg, threads));

    // Adam on every position at once.
    const double rate = 1.0, beta1 = 0.9, beta2 = 0.999, epsilon = 1e-8;
    std::vector<double> m_mg(n), v_mg(n), m_eg(n), v_eg(n);
    std::vector<double> best_mg = mg, best_eg = eg;
    double best_test = mean_error(test, k, mg, eg, threads);
    const double dsig = k * std::log(10.0) / 400.0;

    for (int epoch = 1; epoch <= epochs; ++epoch) {
        std::vector<std::vector<double>> grad_mg(threads, std::vector<double>(n)), grad_eg(threads, std::vector<double>(n));
        parallel(train.size(), threads, [&](size_t a, size_t b, int t) {
            auto& gm = grad_mg[t];
            auto& ge = grad_eg[t];
            for (size_t i = a; i < b; ++i) {
                const double s = sigmoid(k, evaluate(train, i, mg, eg));
                // d(error^2)/d(eval)
                const double g = -2.0 * (train.result[i] - s) * s * (1.0 - s) * dsig;
                const double wm = g * train.mg_weight[i], we = g * train.eg_weight[i];
                for (uint32_t j = train.begin[i]; j < train.begin[i + 1]; ++j) {
                    gm[train.index[j]] += wm * train.coefficient[j];
                    ge[train.index[j]] += we * train.coefficient[j];
                }
            }
        });
        const double scale = 1.0 / double(train.size());
        const double c1 = 1.0 - std::pow(beta1, epoch), c2 = 1.0 - std::pow(beta2, epoch);
        for (size_t i = 0; i < n; ++i) {
            double gm = 0, ge = 0;
            for (int t = 0; t < threads; ++t) { gm += grad_mg[t][i]; ge += grad_eg[t][i]; }
            gm *= scale;
            ge *= scale;
            if (params[i].tune_mg) {
                m_mg[i] = beta1 * m_mg[i] + (1 - beta1) * gm;
                v_mg[i] = beta2 * v_mg[i] + (1 - beta2) * gm * gm;
                mg[i] -= rate * (m_mg[i] / c1) / (std::sqrt(v_mg[i] / c2) + epsilon);
            }
            if (params[i].tune_eg) {
                m_eg[i] = beta1 * m_eg[i] + (1 - beta1) * ge;
                v_eg[i] = beta2 * v_eg[i] + (1 - beta2) * ge * ge;
                eg[i] -= rate * (m_eg[i] / c1) / (std::sqrt(v_eg[i] / c2) + epsilon);
            }
        }

        if (epoch % 100 == 0 || epoch == epochs) {
            const double train_error = mean_error(train, k, mg, eg, threads);
            const double test_error = mean_error(test, k, mg, eg, threads);
            if (test_error < best_test) { best_test = test_error; best_mg = mg; best_eg = eg; }
            std::printf("epoch %5d  error %.6f  held out %.6f  (%.0f s)\n", epoch, train_error, test_error, seconds());
            std::fflush(stdout);
        }
    }

    // Keep the weights that did best on the positions the tuner never saw.
    std::ofstream out(output);
    out << Eval::Tune::source(best_mg, best_eg);
    std::printf("best held-out error %.6f; tuned weights written to %s\n", best_test, output.c_str());

    // The largest changes, as a summary.
    std::vector<std::pair<double, size_t>> changes;
    for (size_t i = 0; i < n; ++i)
        changes.emplace_back(std::abs(best_mg[i] - params[i].mg) + std::abs(best_eg[i] - params[i].eg), i);
    std::sort(changes.rbegin(), changes.rend());
    std::printf("largest changes (before -> after, middlegame / endgame; tables before re-centring):\n");
    for (size_t j = 0; j < std::min<size_t>(15, changes.size()); ++j) {
        const size_t i = changes[j].second;
        std::printf("  %-24s %5d/%-5d -> %5.0f/%-5.0f\n", params[i].name.c_str(), params[i].mg, params[i].eg,
                    best_mg[i], best_eg[i]);
    }
    return 0;
}

} // namespace Tune

#endif
