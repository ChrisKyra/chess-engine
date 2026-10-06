CXX      ?= g++
# -pthread: the search runs on its own thread.
CXXFLAGS ?= -O3 -std=c++17 -Wall -Wextra -DNDEBUG -flto -pthread
LDFLAGS  ?= -flto -pthread

# -march=native gives the best popcount/bitscan instructions on the build
# machine. Drop it if you need a binary that runs on other CPUs.
NATIVE   ?= -march=native

SRC = bitboard.cpp bitbase.cpp position.cpp movegen.cpp eval.cpp search.cpp perft.cpp uci.cpp
OBJ = $(SRC:.cpp=.o)
BIN = engine

all: $(BIN)

$(BIN): $(OBJ)
	$(CXX) $(CXXFLAGS) $(NATIVE) $(LDFLAGS) -o $@ $(OBJ)

%.o: %.cpp
	$(CXX) $(CXXFLAGS) $(NATIVE) -c $< -o $@

test: $(BIN)
	./$(BIN) test

bench: $(BIN)
	./$(BIN) bench

# The tuner: the engine built with the evaluation trace (EVAL_TRACE) plus the
# Texel tuner, as a separate binary so the engine itself stays as fast as ever.
#   make tuner && ./tuner tune positions.txt [epochs] [output]
TUNER = tuner
TUNER_OBJ = $(SRC:%.cpp=tuner-%.o) tuner-tune.o

$(TUNER): $(TUNER_OBJ)
	$(CXX) $(CXXFLAGS) $(NATIVE) $(LDFLAGS) -o $@ $(TUNER_OBJ)

tuner-%.o: %.cpp
	$(CXX) $(CXXFLAGS) $(NATIVE) -DEVAL_TRACE -c $< -o $@

# The SPSA build: the engine with its search parameters exposed as UCI options
# (SEARCH_TUNE), for tuning them with the match runner's --spsa mode.
#   make spsa && ../tools/match-runner/... --spsa params.txt --engine1 ./engine-spsa ...
SPSA = engine-spsa
SPSA_OBJ = $(SRC:%.cpp=spsa-%.o)

$(SPSA): $(SPSA_OBJ)
	$(CXX) $(CXXFLAGS) $(NATIVE) $(LDFLAGS) -o $@ $(SPSA_OBJ)

spsa-%.o: %.cpp
	$(CXX) $(CXXFLAGS) $(NATIVE) -DSEARCH_TUNE -c $< -o $@

spsa: $(SPSA)

clean:
	rm -f $(OBJ) $(BIN) $(TUNER_OBJ) $(TUNER) $(SPSA_OBJ) $(SPSA)

.PHONY: all test bench clean spsa
